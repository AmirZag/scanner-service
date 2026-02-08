using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScannerService.TrayApp.Services;

public class WebApiHostService : IDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly int _port;
    private bool _isDisposed;

    private static readonly CompositeFormat DataSourceFormat = CompositeFormat.Parse("Data Source={0}");
    private static readonly CompositeFormat WebApiStartedFormat = CompositeFormat.Parse("Web API started on port {0}");
    private static readonly CompositeFormat WebApiFailedFormat = CompositeFormat.Parse("Failed to start Web API: {0}");
    private static readonly CompositeFormat WebApiStopErrorFormat = CompositeFormat.Parse("Error stopping Web API: {0}");

    public bool IsRunning { get; private set; }

    public WebApiHostService(int port)
    {
        _port = port;
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

            var logPath = Path.Combine(AppContext.BaseDirectory, loggingConfig.File.Path);
            var logDirectory = Path.GetDirectoryName(logPath);

            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var rollingInterval = loggingConfig.File.RollingInterval.ToLowerInvariant() switch
            {
                "minute" => RollingInterval.Minute,
                "hour" => RollingInterval.Hour,
                "day" => RollingInterval.Day,
                "month" => RollingInterval.Month,
                "year" => RollingInterval.Year,
                _ => RollingInterval.Day
            };

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logPath,
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: rollingInterval,
                    retainedFileCountLimit: loggingConfig.File.RetainedFileCountLimit,
                    fileSizeLimitBytes: loggingConfig.File.FileSizeLimitBytes,
                    rollOnFileSizeLimit: loggingConfig.File.RollOnFileSizeLimit,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            builder.Host.UseSerilog();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(_port);
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

            builder.Services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();

            builder.Services.AddEndpointsApiExplorer();

            // Configure NSwag with document name "openapi"
            builder.Services.AddOpenApiDocument(config =>
            {
                config.DocumentName = "openapi";
                config.Title = "Scanner Service API";
                config.Version = "v1.0.0";
                config.Description = "API for managing scanners, profiles, and scanning operations";
            });

            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            _app = builder.Build();

            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<Context>().Database.EnsureCreatedAsync();
            }

            // Serve the OpenAPI JSON at /openapi/openapi.json
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

            _app.UseCors();

            ConfigureEndpoints(_app);

            _runTask = _app.RunAsync(_cts.Token);
            IsRunning = true;

            Log.Information("Scanner Service API started successfully on port {Port}", _port);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiStartedFormat, _port));
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
        {
            Log.Error(ex,
                "Port {Port} is already in use. Please close any other instances of the application or change the port in appsettings.json",
                _port);

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture,
                    "Port {0} is already in use. Please close other instances or change the port.", _port),
                ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Web API on port {Port}", _port);
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

        app.MapGet("/api/scanners", async (IScannerQueries svc) =>
            Results.Ok(await svc.GetScannersListAsync()))
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

        app.MapPost("/api/profiles", async (UpsertProfileDto req, IProfileRepository svc, IValidator<UpsertProfileDto> validator) =>
        {
            var validationResult = await validator.ValidateAsync(req);
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

        app.MapPut("/api/profiles/{id}", async (int id, UpsertProfileDto req, IProfileRepository svc, IValidator<UpsertProfileDto> validator) =>
        {
            var validationResult = await validator.ValidateAsync(req);
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
        .Accepts<UpsertProfileDto>("application/json");

        app.MapDelete("/api/profiles/{id}", async (int id, IProfileRepository svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/scan", async (ScanRequestDto req, IScanJobService svc, IValidator<ScanRequestDto> validator) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await svc.StartScanJobAsync(req);

            if (!result.Success)
            {
                return Results.BadRequest(new { result.ErrorMessage, result.Duration });
            }

            return Results.File(result.FileContent!, result.ContentType!, result.FileName!);
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

        app.MapPut("/api/export-settings", async (ExportSettingDto dto, IExportSettingRepository svc, IValidator<ExportSettingDto> validator) =>
        {
            var validationResult = await validator.ValidateAsync(dto);
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
}
