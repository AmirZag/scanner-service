using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using FluentValidation;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Extension methods to configure API endpoints by category.
/// Improves maintainability by breaking down endpoint configuration into focused methods.
/// </summary>
public static class EndpointConfigurationExtensions
{
    /// <summary>
    /// Configures all API endpoints for the application.
    /// </summary>
    public static void ConfigureAllEndpoints(this WebApplication app)
    {
        app.ConfigureHealthEndpoints();
        app.ConfigureScannerEndpoints();
        app.ConfigureProfileEndpoints();
        app.ConfigureScanEndpoints();
        app.ConfigureExportSettingsEndpoints();
        app.ConfigureRecentScansEndpoints();
    }

    /// <summary>
    /// Configures health check endpoints.
    /// </summary>
    public static void ConfigureHealthEndpoints(this WebApplication app)
    {
        // Simple health check
        app.MapGet("/api/health", () =>
            Results.Ok(new ApiHealthCheckDto(true, "1.0.0")))
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<ApiHealthCheckDto>(StatusCodes.Status200OK);

        // Detailed health check with dependency status
        app.MapGet("/api/health/detailed", async (
            HttpContext context,
            Infrastructure.Persistence.Context db,
            IScannerQueries scanner,
            CancellationToken ct) =>
        {
            var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
            var dependencies = new Dictionary<string, bool>();

            // Check database connectivity
            try
            {
                dependencies["Database"] = await db.Database.CanConnectAsync(ct);
            }
            catch
            {
                dependencies["Database"] = false;
            }

            // Check scanner availability
            try
            {
                var scanners = await scanner.GetScannersListAsync(ct);
                dependencies["Scanners"] = scanners.Count > 0;
            }
            catch
            {
                dependencies["Scanners"] = false;
            }

            var isHealthy = dependencies.Values.All(v => v);
            var result = isHealthy
                ? DetailedApiHealthCheckDto.Healthy("1.0.0", dependencies, correlationId)
                : DetailedApiHealthCheckDto.Unhealthy("1.0.0", dependencies, correlationId);

            if (isHealthy)
            {
                return TypedResults.Ok(result);
            }

            // For unhealthy status, return 503 with the error details
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return TypedResults.Ok(result);
        })
        .WithName("GetDetailedHealth")
        .WithTags("Health")
        .Produces<DetailedApiHealthCheckDto>(StatusCodes.Status200OK)
        .Produces<DetailedApiHealthCheckDto>(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Configures scanner-related endpoints.
    /// </summary>
    public static void ConfigureScannerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/scanners", async (IScannerQueries svc, CancellationToken ct) =>
            Results.Ok(await svc.GetScannersListAsync(ct)))
            .WithName("GetAllScanners")
            .WithTags("Scanners")
            .Produces(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Configures profile management endpoints.
    /// </summary>
    public static void ConfigureProfileEndpoints(this WebApplication app)
    {
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

            var p = await svc.AddAsync(req, ct);
            return Results.Created($"/api/profiles/{p.Id}", p);
        })
        .WithName("CreateProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .Accepts<UpsertProfileDto>("application/json");

        app.MapPatch("/api/profiles/{id}", async (int id, UpdateProfileDto req, IProfileRepository svc, IValidator<UpdateProfileDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(req, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var p = await svc.UpdateAsync(id, req, ct);
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
    }

    /// <summary>
    /// Configures scan execution endpoints.
    /// </summary>
    public static void ConfigureScanEndpoints(this WebApplication app)
    {
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
    }

    /// <summary>
    /// Configures export settings endpoints.
    /// </summary>
    public static void ConfigureExportSettingsEndpoints(this WebApplication app)
    {
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

            await svc.UpdateExportSettingAsync(dto, ct);
            return Results.Ok();
        })
        .WithName("UpdateExportSettings")
        .WithTags("Export Settings")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Accepts<ExportSettingDto>("application/json");
    }

    /// <summary>
    /// Configures recent scans endpoints.
    /// </summary>
    public static void ConfigureRecentScansEndpoints(this WebApplication app)
    {
        app.MapGet("/api/recent-scans/{count:int:min(1):max(100)}", async (int count, IRecentScansService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRecentScansAsync(count, ct)))
            .WithName("GetRecentScans")
            .WithTags("Recent Scans")
            .Produces<RecentScansResponseDto>(StatusCodes.Status200OK);
    }
}
