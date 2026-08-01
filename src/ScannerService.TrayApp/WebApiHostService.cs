using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NJsonSchema;
using Scalar.AspNetCore;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using ScannerService.Infrastructure.Services;
using ScannerService.TrayApp.Configurations;
using Serilog;
using Serilog.Events;

namespace ScannerService.TrayApp;

public class WebApiHostService : IDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly int _requestedPort;
    private bool _isDisposed;

    private static readonly CompositeFormat DataSourceFormat = CompositeFormat.Parse("Data Source={0}");
    private static readonly CompositeFormat WebApiStartedFormat = CompositeFormat.Parse("Web API started on port {0}");
    private static readonly CompositeFormat WebApiFailedFormat = CompositeFormat.Parse("Failed to start Web API: {0}");
    private static readonly CompositeFormat WebApiStopErrorFormat = CompositeFormat.Parse("Error stopping Web API: {0}");

    public bool IsRunning { get; private set; }
    public int ActualPort { get; private set; }

    public WebApiHostService(int port)
    {
        _requestedPort = port;
        ActualPort = port;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            });

            var loggingConfig = builder.Configuration.GetSection("Logging")
                .Get<LoggingConfiguration>() ?? new LoggingConfiguration();

            var logValidation = Configurations.ConfigurationValidator.ValidateLoggingConfiguration(loggingConfig);
            if (!logValidation.IsValid)
            {
                throw new InvalidOperationException("Logging configuration validation failed: " + string.Join("; ", logValidation.Errors));
            }

            SerilogConfigurationExtensions.InitializeSerilog(loggingConfig, AppContext.BaseDirectory);
            builder.Host.UseSerilog();

            var availablePort = await FindAvailablePortAsync(_requestedPort);
            if (availablePort != _requestedPort)
            {
                Log.Warning("Requested port {RequestedPort} is in use, using alternative port {AvailablePort}", _requestedPort, availablePort);
            }
            ActualPort = availablePort;

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(ActualPort);
                // Limit max request body size to 100 MB
                options.Limits.MaxRequestBodySize = 104857600; // 100 MB
            });

            var dbPath = Path.Combine(AppContext.BaseDirectory, "scanner.db");
            builder.Services.AddDbContext<Context>(options =>
                options.UseSqlite(string.Format(CultureInfo.InvariantCulture, DataSourceFormat, dbPath)));

            builder.Services.AddSingleton<Infrastructure.Services.ScannerService>();
            builder.Services.AddSingleton<IScannerQueries>(sp => sp.GetRequiredService<Infrastructure.Services.ScannerService>());
            builder.Services.AddSingleton<IScannerService>(sp => sp.GetRequiredService<Infrastructure.Services.ScannerService>());
            builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
            builder.Services.AddScoped<IExportSettingRepository, ExportSettingRepository>();
            builder.Services.AddScoped<IScanJobService, ScanJobService>();
            builder.Services.AddScoped<IRecentScansService, Infrastructure.Services.RecentScansService>();
            builder.Services.AddMemoryCache();

            builder.Services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();

            builder.Services.AddHttpClient();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApiDocument(config =>
            {
                config.DocumentName = "openapi";
                config.Title = "Scanner Service API";
                config.Version = "v1.0.0";
                config.Description = "API for managing scanners, profiles, and scanning operations";
            });

            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.SetIsOriginAllowed(OriginChecker.IsLocalOrigin)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()));

            _app = builder.Build();

            _app.Use(async (context, next) =>
            {
                // For scan requests, we might receive larger payloads
                if (context.Request.Path.StartsWithSegments("/api/scan"))
                {
                    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = Domain.Common.ApplicationConstants.FileSizes.OneHundredMegabytes;
                }
                else
                {
                    // For other endpoints, limit to 1 MB
                    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = Domain.Common.ApplicationConstants.FileSizes.OneMegabyte;
                }
                await next();
            });

            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<Context>().Database.EnsureCreatedAsync(_cts?.Token ?? CancellationToken.None);
            }

            _app.UseOpenApi(options =>
            {
                options.Path = "/openapi/{documentName}.json";
            });

            // Configure Scalar to point to the correct OpenAPI spec
            _app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("Scanner Service API")
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .WithOpenApiRoutePattern("/openapi/{documentName}.json");
            });

            _app.UseMiddleware<Middleware.RateLimitMiddleware>(new Middleware.RateLimitOptions
            {
                MaxRequests = Domain.Common.ApplicationConstants.RateLimit.DefaultMaxRequests,
                Window = TimeSpan.FromMinutes(Domain.Common.ApplicationConstants.RateLimit.DefaultWindowMinutes)
            });
            _app.Use(async (context, next) =>
            {
                var startTime = DateTime.UtcNow;
                Log.Debug("API Request - Method: {Method}, Path: {Path}, RemoteIP: {RemoteIP}",
                    context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "API request failed - Method: {Method}, Path: {Path}, Status: {StatusCode}",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode);
                    throw;
                }
                finally
                {
                    var duration = DateTime.UtcNow - startTime;
                    Log.Information("API Request completed - Method: {Method}, Path: {Path}, Status: {StatusCode}, Duration: {DurationMs}ms",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode, duration.TotalMilliseconds);
                }
            });

            _app.UseCors();

            ConfigureEndpoints(_app);

            _runTask = _app.RunAsync(_cts?.Token ?? CancellationToken.None);
            IsRunning = true;

            Log.Information("Scanner Service API started successfully on port {ActualPort} (requested: {RequestedPort})", ActualPort, _requestedPort);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiStartedFormat, ActualPort));
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
        {
            Log.Error(ex,
                "Port {Port} is already in use. Please close any other instances of the application or change the port in appsettings.json",
                ActualPort);

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture,
                    "Port {0} is already in use. Please close other instances or change the port.", ActualPort),
                ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Web API on port {ActualPort}", ActualPort);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiFailedFormat, ex.Message));
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            Log.Information("Stopping Scanner Service API");

            _cts?.CancelAsync();

            if (_runTask != null)
            {
                await _runTask.ConfigureAwait(false);
            }

            if (_app != null)
            {
                await _app.DisposeAsync();
                _app = null;
            }

            IsRunning = false;
            Log.Information("Scanner Service API stopped successfully");
            Debug.WriteLine("Web API stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping Web API");
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiStopErrorFormat, ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
        }
    }

    private void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/api/health", () =>
            Results.Ok(new ApiHealthCheckDto(true, "1.0.0")))
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<ApiHealthCheckDto>(StatusCodes.Status200OK);

        app.MapGet("/api/scanners", async (IScannerQueries svc, CancellationToken ct) =>
            Results.Ok(await svc.GetScannersListAsync(ct)))
            .WithName("GetAllScanners")
            .WithTags("Scanners")
            .Produces(StatusCodes.Status200OK);

        app.MapGet("/api/profiles", async (IProfileRepository svc) =>
            Results.Ok(await svc.GetAllAsync()))
            .WithName("GetAllProfiles")
            .WithTags("Profiles")
            .Produces(StatusCodes.Status200OK);

        app.MapGet("/api/profiles/{id}", async (int id, IProfileRepository svc) =>
        {
            var p = await svc.GetByIdAsync(id);
            return p == null ? Results.NotFound() : Results.Ok(p);
        })
        .WithName("GetProfileById")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/profiles", async (UpsertProfileDto req, IProfileRepository svc, IValidator<UpsertProfileDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(req, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var p = await svc.AddAsync(req);
            return Results.Created($"/api/profiles/{p.Id}", p);
        })
        .WithName("CreateProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .Accepts<UpsertProfileDto>("application/json");

        // Partial update endpoint: only provided fields are updated, null fields are unchanged
        app.MapPatch("/api/profiles/{id}", async (int id, UpdateProfileDto req, IProfileRepository svc, IValidator<UpdateProfileDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(req, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var p = await svc.UpdateAsync(id, req);
            return p == null ? Results.NotFound() : Results.Ok(p);
        })
        .WithName("UpdateProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .Accepts<UpdateProfileDto>("application/json");

        app.MapDelete("/api/profiles/{id}", async (int id, IProfileRepository svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/scan", async (ScanRequestDto req, IScanJobService svc, IValidator<ScanRequestDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(req, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await svc.StartScanJobAsync(req, ct);

            if (!result.Success)
            {
                return Results.BadRequest(new { result.ErrorMessage, result.Duration });
            }

            // Stream the file directly from disk instead of loading into memory
            var fileStream = new FileStream(result.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(fileStream, result.ContentType!, result.FileName!);
        })
        .WithName("PerformScan")
        .WithTags("Scan")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
        .Produces(StatusCodes.Status200OK, contentType: "image/png")
        .Produces(StatusCodes.Status200OK, contentType: "application/zip")
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesValidationProblem()
        .Accepts<ScanRequestDto>("application/json");

        app.MapGet("/api/export-settings", async (IExportSettingRepository svc) =>
            Results.Ok(await svc.GetExportSettingAsync()))
            .WithName("GetExportSettings")
            .WithTags("Export Settings")
            .Produces(StatusCodes.Status200OK);

        app.MapPut("/api/export-settings", async (ExportSettingDto dto, IExportSettingRepository svc, IValidator<ExportSettingDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(dto, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            await svc.UpdateExportSettingAsync(dto);
            return Results.Ok();
        })
        .WithName("UpdateExportSettings")
        .WithTags("Export Settings")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Accepts<ExportSettingDto>("application/json");

        app.MapGet("/api/recent-scans/{count:int:min(1):max(100)}", async (int count, IRecentScansService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRecentScansAsync(count, ct)))
        .WithName("GetRecentScans")
        .WithTags("Recent Scans")
        .Produces<RecentScansResponseDto>(StatusCodes.Status200OK);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
        Log.CloseAndFlush();
        GC.SuppressFinalize(this);
    }

    private static Task<int> FindAvailablePortAsync(int startPort)
    {
        // Get actively used TCP ports to avoid checking them
        var usedPorts = new HashSet<int>();

        try
        {
            var tcpConnections = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var listener in tcpConnections)
            {
                if (listener.Address.Equals(IPAddress.Loopback) || listener.Address.Equals(IPAddress.Any))
                {
                    usedPorts.Add(listener.Port);
                }
            }
        }
        catch
        {
            // Fall back to checking ports directly if IPGlobalProperties fails
        }

        // Try the requested port first
        if (!usedPorts.Contains(startPort) && IsPortAvailable(startPort))
        {
            return Task.FromResult(startPort);
        }

        // Try ports in the range (startPort to startPort + MaxPortSearchRange)
        var maxSearch = startPort + Domain.Common.ApplicationConstants.Ports.MaxPortSearchRange;
        for (int port = startPort + 1; port <= maxSearch; port++)
        {
            if (!usedPorts.Contains(port) && IsPortAvailable(port))
            {
                return Task.FromResult(port);
            }
        }

        // If no port found in that range, try any available port
        using var tempListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        var availablePort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();
        return Task.FromResult(availablePort);
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Helper class to validate CORS origins for local network access.
    /// Allows localhost, 127.0.0.1, and private IP ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x).
    /// </summary>
    private static class OriginChecker
    {
        public static bool IsLocalOrigin(string? origin)
        {
            if (string.IsNullOrEmpty(origin))
            {
                return false;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();

            // Allow localhost variants
            if (host == "localhost" || host == "127.0.0.1")
            {
                return true;
            }

            // Allow any 127.x.x.x (loopback range)
            if (host.StartsWith("127.", StringComparison.Ordinal))
            {
                return true;
            }

            // Parse IP address and check if private
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                var bytes = ipAddress.GetAddressBytes();

                // 10.0.0.0 - 10.255.255.255 (Class A private)
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0 - 172.31.255.255 (Class B private)
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0 - 192.168.255.255 (Class C private)
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
