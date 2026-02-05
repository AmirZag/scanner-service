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
using System.IO;
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

    public bool IsRunning { get; private set; }

    public WebApiHostService(int port)
    {
        _port = port;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        try
        {
            _cts = new CancellationTokenSource();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            });

            // Load logging configuration
            var loggingConfig = builder.Configuration.GetSection("Logging")
                .Get<LoggingConfiguration>() ?? new LoggingConfiguration();

            // Configure Serilog from appsettings
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
                    rollingInterval: rollingInterval,
                    retainedFileCountLimit: loggingConfig.File.RetainedFileCountLimit,
                    fileSizeLimitBytes: loggingConfig.File.FileSizeLimitBytes,
                    rollOnFileSizeLimit: loggingConfig.File.RollOnFileSizeLimit,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            builder.Host.UseSerilog();

            // Configure Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(_port);
            });

            // Database configuration
            var dbPath = Path.Combine(AppContext.BaseDirectory, "scanner.db");
            builder.Services.AddDbContext<ScannerDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // Register services
            builder.Services.AddSingleton<ScannerDeviceProvider>();
            builder.Services.AddSingleton<IScannerDevice>(sp => sp.GetRequiredService<ScannerDeviceProvider>());
            builder.Services.AddSingleton<IScannerDeviceService>(sp => sp.GetRequiredService<ScannerDeviceProvider>());
            builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
            builder.Services.AddScoped<IExportSettings, ExportSettingsService>();
            builder.Services.AddScoped<IScanService, ScanService>();

            // Register FluentValidation validators
            builder.Services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();

            // Add OpenAPI/NSwag services
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApiDocument(config =>
            {
                config.DocumentName = "v1";
                config.Title = "Scanner Service API";
                config.Version = "v1.0.0";
                config.Description = "API for managing scanners, profiles, and scanning operations";
            });

            // CORS
            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            _app = builder.Build();

            // Ensure database is created
            using (var scope = _app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<ScannerDbContext>()
                    .Database.EnsureCreated();
            }

            // Use NSwag OpenAPI
            _app.UseOpenApi(options =>
            {
                options.Path = "/openapi/{documentName}.json";
            });

            // Configure Scalar
            _app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("Scanner Service API")
                    .WithTheme(ScalarTheme.Purple)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            });

            _app.UseCors();

            // Configure endpoints
            ConfigureEndpoints(_app);

            // Start the web application
            _runTask = _app.RunAsync(_cts.Token);
            IsRunning = true;

            Log.Information("Scanner Service API started successfully on port {Port}", _port);
            Debug.WriteLine($"Web API started on port {_port}");
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
        {
            Log.Error("Port {Port} is already in use. Please close any other instances of the application or change the port in appsettings.json", _port);
            throw new InvalidOperationException($"Port {_port} is already in use. Please close other instances or change the port.", ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Web API on port {Port}", _port);
            Debug.WriteLine($"Failed to start Web API: {ex.Message}");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        try
        {
            Log.Information("Stopping Scanner Service API");

            _cts?.Cancel();

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
            Debug.WriteLine($"Error stopping Web API: {ex.Message}");
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
        // Health Check Endpoint
        app.MapGet("/api/health", () =>
            Results.Ok(new HealthDto(true, "1.0.0")))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Check API health status")
            .WithDescription("Returns the health status and version of the Scanner Service API")
            .Produces<HealthDto>(StatusCodes.Status200OK);

        // Scanner Endpoints
        app.MapGet("/api/scanners", async (IScannerDevice svc) =>
            Results.Ok(await svc.GetScannersAsync()))
            .WithName("GetAllScanners")
            .WithTags("Scanners")
            .WithSummary("Get all available scanners")
            .WithDescription("Retrieves a list of all scanners connected to the system")
            .Produces(StatusCodes.Status200OK);

        // Profile Endpoints
        app.MapGet("/api/profiles", async (IProfileRepository svc) =>
            Results.Ok(await svc.GetAllAsync()))
            .WithName("GetAllProfiles")
            .WithTags("Profiles")
            .WithSummary("Get all scan profiles")
            .WithDescription("Retrieves all saved scan profile configurations")
            .Produces(StatusCodes.Status200OK);

        app.MapGet("/api/profiles/{id}", async (int id, IProfileRepository svc) =>
        {
            var p = await svc.GetByIdAsync(id);
            return p == null ? Results.NotFound() : Results.Ok(p);
        })
        .WithName("GetProfileById")
        .WithTags("Profiles")
        .WithSummary("Get a specific profile by ID")
        .WithDescription("Retrieves the details of a specific scan profile")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/profiles", async (UpsertProfile req, IProfileRepository svc, IValidator<UpsertProfile> validator) =>
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
        .WithSummary("Create a new scan profile")
        .WithDescription("Creates a new scan profile with the specified settings")
        .Produces(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .Accepts<UpsertProfile>("application/json");

        app.MapPut("/api/profiles/{id}", async (int id, UpsertProfile req, IProfileRepository svc, IValidator<UpsertProfile> validator) =>
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
        .WithSummary("Update an existing profile")
        .WithDescription("Updates the settings of an existing scan profile")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .Accepts<UpsertProfile>("application/json");

        app.MapDelete("/api/profiles/{id}", async (int id, IProfileRepository svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProfile")
        .WithTags("Profiles")
        .WithSummary("Delete a profile")
        .WithDescription("Permanently deletes a scan profile")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // Scan Endpoint
        app.MapPost("/api/scan", async (ScanRequest req, IScanService svc, IValidator<ScanRequest> validator) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await svc.ScanAsync(req);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("PerformScan")
        .WithTags("Scan")
        .WithSummary("Perform a scan operation")
        .WithDescription("Initiates a scan using the specified scanner and profile settings")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesValidationProblem()
        .Accepts<ScanRequest>("application/json");

        // ExportSettings Endpoints
        app.MapGet("/api/export-settings", async (IExportSettings svc) =>
            Results.Ok(await svc.GetExportSettingsAsync()))
            .WithName("GetExportSettings")
            .WithTags("Export Settings")
            .WithSummary("Get current export settings")
            .WithDescription("Retrieves the current file export and save settings")
            .Produces(StatusCodes.Status200OK);

        app.MapPut("/api/export-settings", async (ExportSettingsDto dto, IExportSettings svc, IValidator<ExportSettingsDto> validator) =>
        {
            var validationResult = await validator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            await svc.UpdateExportSettingsAsync(dto);
            return Results.Ok();
        })
        .WithName("UpdateExportSettings")
        .WithTags("Export Settings")
        .WithSummary("Update export settings")
        .WithDescription("Updates the file export and save location settings")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Accepts<ExportSettingsDto>("application/json");
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
        Log.CloseAndFlush();
    }
}