using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private readonly ScannerServiceConfiguration _config;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private bool _isDisposed;

    private static readonly CompositeFormat DataSourceFormat = CompositeFormat.Parse("Data Source={0}");
    private static readonly CompositeFormat WebApiStartedFormat = CompositeFormat.Parse("Web API started on port {0}");
    private static readonly CompositeFormat WebApiFailedFormat = CompositeFormat.Parse("Failed to start Web API: {0}");
    private static readonly CompositeFormat WebApiStopErrorFormat = CompositeFormat.Parse("Error stopping Web API: {0}");

    public bool IsRunning { get; private set; }
    public int ActualPort { get; private set; }

    public WebApiHostService(ScannerServiceConfiguration config)
    {
        _config = config;
        ActualPort = config.ApiPort;
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

            var availablePort = await FindAvailablePortAsync(_config.ApiPort);
            if (availablePort != _config.ApiPort)
            {
                Log.Warning("Requested port {RequestedPort} is in use, using alternative port {AvailablePort}", _config.ApiPort, availablePort);
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

            // Bounded budgets for scanner discovery/scanning (drivers can hang on offline devices)
            builder.Services.AddSingleton(new Domain.Common.ScannerTimeouts
            {
                DriverTimeoutMs = _config.DriverTimeoutMs,
                EsclSearchTimeoutMs = _config.EsclSearchTimeoutMs,
                EsclSearchMarginMs = _config.EsclSearchMarginMs,
                DriverCooldownMs = _config.DriverCooldownMs,
                DriverCooldownMaxMs = _config.DriverCooldownMaxMs,
                ScanQueueTimeoutMs = _config.ScanQueueTimeoutMs,
                ScanOverallTimeoutMs = _config.ScanOverallTimeoutMs,
                ScanNoProgressTimeoutMs = _config.ScanNoProgressTimeoutMs,
                ShutdownTimeoutMs = _config.ShutdownTimeoutMs
            });

            // Bound the host's graceful-shutdown drain (the 30s default lets a wedged request stall exit)
            builder.Services.Configure<HostOptions>(options =>
                options.ShutdownTimeout = TimeSpan.FromMilliseconds(_config.ShutdownTimeoutMs));

            // Hard backstop so HTTP clients always receive a status code even when a native scanner
            // call ignores cancellation; registered policies are applied per-endpoint.
            builder.Services.AddRequestTimeouts(options =>
            {
                options.AddPolicy(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scanners,
                    TimeSpan.FromSeconds(_config.ScannersRequestTimeoutSeconds));
                options.AddPolicy(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scan,
                    TimeSpan.FromSeconds(_config.ScanRequestTimeoutSeconds));
            });

            // Scanner services with proper lifetime management
            builder.Services.AddSingleton<Infrastructure.Services.ScannerInitializer>();
            builder.Services.AddSingleton<IScannerInitializer>(sp => sp.GetRequiredService<Infrastructure.Services.ScannerInitializer>());
            builder.Services.AddSingleton<Infrastructure.Services.ScannerService>();
            builder.Services.AddSingleton<IScannerQueries>(sp =>
            {
                var scannerService = sp.GetRequiredService<Infrastructure.Services.ScannerService>();
                var memoryCache = sp.GetRequiredService<IMemoryCache>();
                var logger = sp.GetRequiredService<ILogger<Infrastructure.Services.CachedScannerService>>();
                return new Infrastructure.Services.CachedScannerService(scannerService, memoryCache, logger);
            });
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
                    p.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()));

            _app = builder.Build();

            // First middleware: enforces the registered request timeout policies (408 backstop)
            _app.UseRequestTimeouts();

            // Request body size configuration
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

            // Correlation ID middleware for request tracing
            _app.UseMiddleware<Middleware.CorrelationIdMiddleware>();

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

            _app.ConfigureAllEndpoints();

            _runTask = _app.RunAsync(_cts?.Token ?? CancellationToken.None);
            IsRunning = true;

            Log.Information("Scanner Service API started successfully on port {ActualPort} (requested: {RequestedPort})", ActualPort, _config.ApiPort);
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

        // Reentrancy-safe: concurrent stop attempts (e.g. menu + dispose racing) are ignored.
#pragma warning disable S8949 // Deliberately token-free: the shutdown CTS is already cancelled here and must not void the bound
        if (!await _stopGate.WaitAsync(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            Log.Information("Stopping Scanner Service API");

            _cts?.CancelAsync();

            if (_runTask != null)
            {
                // Bound the wait: a wedged in-flight request must not block shutdown indefinitely.
                Task winner = await Task.WhenAny(_runTask, Task.Delay(_config.ShutdownTimeoutMs)).ConfigureAwait(false);
#pragma warning restore S8949
                if (winner == _runTask)
                {
                    await _runTask.ConfigureAwait(false);
                }
                else
                {
                    Log.Error("Web API host did not stop within {TimeoutMs}ms; continuing shutdown", _config.ShutdownTimeoutMs);
                }
            }

            // Clean up temporary files before disposing the app
            if (_app != null)
            {
                using var scope = _app.Services.CreateScope();
                var scanJobService = scope.ServiceProvider.GetService<IScanJobService>() as ScanJobService;
                scanJobService?.CleanupOldTempFiles(TimeSpan.Zero);
            }

            if (_app != null)
            {
                await _app.DisposeAsync().ConfigureAwait(false);
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
            _stopGate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // Bound the whole stop: container dispose includes scanner worker teardown, which can block on
        // a hung driver even after the host itself has stopped. StopAsync swallows its own exceptions.
#pragma warning disable S8949 // Deliberately token-free: there is no valid ambient token during final dispose
        var stopTask = Task.Run(StopAsync);
        int shutdownBoundMs = _config.ShutdownTimeoutMs * 3;
        var winner = Task.WhenAny(stopTask, Task.Delay(shutdownBoundMs)).GetAwaiter().GetResult();
#pragma warning restore S8949
        if (winner != stopTask)
        {
            Log.Error("Web API stop did not complete within {TimeoutMs}ms; continuing shutdown", shutdownBoundMs);
        }

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
