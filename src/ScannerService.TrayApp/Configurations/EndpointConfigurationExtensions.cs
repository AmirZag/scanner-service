using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.TrayApp;
using FluentValidation;
using Serilog;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Extension methods to configure API endpoints by category.
/// Improves maintainability by breaking down endpoint configuration into focused methods.
/// </summary>
public static class EndpointConfigurationExtensions
{
    /// <summary>Application version reported by the API: &lt;VersionPrefix&gt; from Directory.Build.props
    /// plus the automatic git-commit-count revision appended at build time (e.g. "1.1.0.84").</summary>
    private static readonly string ApiVersion =
        (typeof(EndpointConfigurationExtensions).Assembly.GetName().Version ?? new Version(1, 0, 0)).ToString();
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
        app.ConfigureSettingsEndpoints();
    }

    /// <summary>
    /// Configures health check endpoints.
    /// </summary>
    public static void ConfigureHealthEndpoints(this WebApplication app)
    {
        // Simple health check
        app.MapGet("/api/health", () =>
            Results.Ok(new ApiHealthCheckDto(true, ApiVersion)))
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
                ? DetailedApiHealthCheckDto.Healthy(ApiVersion, dependencies, correlationId)
                : DetailedApiHealthCheckDto.Unhealthy(ApiVersion, dependencies, correlationId);

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
        .Produces<DetailedApiHealthCheckDto>(StatusCodes.Status503ServiceUnavailable)
        .Produces(StatusCodes.Status408RequestTimeout)
        .WithRequestTimeout(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scanners);
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
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status408RequestTimeout)
            .WithRequestTimeout(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scanners);

        // Device enumeration is expensive and its result is cached; clients must clear the cache after
        // scanner or network configuration changes instead of waiting out the cache TTL.
        app.MapPost("/api/scanners/refresh", (IScannerListCache cache) =>
        {
            cache.ClearScannerListCache();
            return Results.Ok(new { message = "Scanner list cache cleared; the next request re-enumerates devices" });
        })
        .WithName("RefreshScanners")
        .WithTags("Scanners")
        .Produces(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Configures profile management endpoints.
    /// </summary>
    public static void ConfigureProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/profiles", async (IProfileRepository svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllAsync(ct)))
            .WithName("GetAllProfiles")
            .WithTags("Profiles")
            .Produces(StatusCodes.Status200OK);

        app.MapGet("/api/profiles/{id}", async (int id, IProfileRepository svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result == null
                ? Results.NotFound(new { error = $"Profile {id} not found" })
                : Results.Ok(result);
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

            var result = await svc.UpdateAsync(id, req, ct);
            return result.IsFailure
                ? Results.NotFound(new { error = result.Error })
                : Results.Ok(result.Value);
        })
        .WithName("UpdateProfile")
        .WithTags("Profiles")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .Accepts<UpdateProfileDto>("application/json");

        app.MapDelete("/api/profiles/{id}", async (int id, IProfileRepository svc, CancellationToken ct) =>
        {
            var result = await svc.DeleteAsync(id, ct);
            return result.IsFailure
                ? Results.NotFound(new { error = result.Error })
                : Results.NoContent();
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
        app.MapPost("/api/scan", async (ScanRequestDto req, IScanJobService svc, IValidator<ScanRequestDto> validator, HttpContext context, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(req, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await svc.StartScanJobAsync(req, ct);

            if (result.IsFailure)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            var scanResult = result.Value!;
            // Multi-page scans stream a temporary aggregation zip that the job service tracks; delete
            // it once the response has been sent. Files outside temp tracking (the exported scans in
            // ExportPath) are deliberately kept - CleanupTempFile only touches tracked temp files.
            context.Response.OnCompleted(() =>
            {
                svc.CleanupTempFile(scanResult.FilePath!);
                return Task.CompletedTask;
            });

            // Stream the file directly from disk using the path overload for proper disposal
            return Results.File(scanResult.FilePath!, scanResult.ContentType!, scanResult.FileName!);
        })
        .WithName("PerformScan")
        .WithTags("Scan")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
        .Produces(StatusCodes.Status200OK, contentType: "image/png")
        .Produces(StatusCodes.Status200OK, contentType: "application/zip")
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesValidationProblem()
        .Accepts<ScanRequestDto>("application/json")
        .WithRequestTimeout(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scan);
    }

    /// <summary>
    /// Configures export settings endpoints.
    /// </summary>
    public static void ConfigureExportSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/export-settings", async (IExportSettingRepository svc, CancellationToken ct) =>
        {
            var result = await svc.GetExportSettingAsync(ct);
            return result.IsFailure
                ? Results.BadRequest(new { error = result.Error })
                : Results.Ok(result.Value);
        })
        .WithName("GetExportSettings")
        .WithTags("Export Settings")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapPut("/api/export-settings", async (ExportSettingDto dto, IExportSettingRepository svc, IValidator<ExportSettingDto> validator, CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(dto, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var result = await svc.UpdateExportSettingAsync(dto, ct);
            return result.IsFailure
                ? Results.BadRequest(new { error = result.Error })
                : Results.Ok();
        })
        .WithName("UpdateExportSettings")
        .WithTags("Export Settings")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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

    /// <summary>
    /// Configures settings endpoints: GET returns the currently effective settings, PUT accepts
    /// the same flat shape as a full replacement (persisted to the appsettings.local.json sidecar),
    /// and DELETE removes all overrides (back to the appsettings.json values). Persisting changes
    /// restarts the API host in-process after the response completes (settings are baked into
    /// host singletons), so PUT/DELETE reply first and the caller re-polls /api/health.
    /// </summary>
    public static void ConfigureSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings", (LocalSettingsStore store) =>
        {
            Result<ScannerSettingsOverridesDto> overrides = store.ReadOverrides();
            if (overrides.IsFailure)
            {
                // Corrupt sidecar: serve the appsettings.json values (startup/restart quarantine
                // and logging already report the problem; the screen must still render).
                Log.Warning("Settings sidecar could not be read; serving appsettings.json values: {Error}", overrides.Error);
            }

            ScannerServiceConfiguration effective = overrides.IsSuccess
                ? ScannerSettingsMapper.Merge(store.BaseConfig, overrides.Value!)
                : store.BaseConfig;

            // Sidecar values that fail validation (hand-edited) are ignored at startup — serve
            // the values the app actually runs with (the base), not the rejected ones.
            (bool IsValid, List<string> Errors) effectiveValidation = Configurations.ConfigurationValidator.ValidateScannerServiceConfiguration(effective);
            if (!effectiveValidation.IsValid)
            {
                Log.Warning("Serving appsettings.json settings; the sidecar values are invalid: {Errors}", string.Join("; ", effectiveValidation.Errors));
                effective = store.BaseConfig;
            }

            return Results.Ok(ScannerSettingsMapper.ToSettingsDto(effective));
        })
        .WithName("GetSettings")
        .WithTags("Settings")
        .Produces<ScannerSettingsDto>(StatusCodes.Status200OK);

        app.MapPut("/api/settings", async (
            ScannerSettingsDto dto,
            LocalSettingsStore store,
            IApiHostControl apiHost,
            IValidator<ScannerSettingsDto> validator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(dto, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            ScannerServiceConfiguration candidate = ScannerSettingsMapper.ToConfiguration(store.BaseConfig, dto);
            (bool IsValid, List<string> Errors) configValidation = Configurations.ConfigurationValidator.ValidateScannerServiceConfiguration(candidate);
            if (!configValidation.IsValid)
            {
                return Results.ValidationProblem(ScannerSettingsMapper.ToValidationProblem(configValidation));
            }

            Result writeResult = await store.WriteOverrides(ScannerSettingsMapper.ToOverridesDto(dto), ct);
            if (writeResult.IsFailure)
            {
                // Nothing persisted, so nothing to apply: the running host stays untouched.
                return Results.Problem(title: "Failed to persist settings", detail: writeResult.Error, statusCode: StatusCodes.Status500InternalServerError);
            }

            ScheduleHostRestart(context, apiHost);
            return Results.Ok(new SettingsUpdateResponseDto(
                true,
                "Settings saved; the API host is restarting with the new values. In-flight scans are aborted; poll /api/health until it responds again."));
        })
        .WithName("UpdateSettings")
        .WithTags("Settings")
        .Produces<SettingsUpdateResponseDto>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status500InternalServerError)
        .Accepts<ScannerSettingsDto>("application/json");

        app.MapDelete("/api/settings/overrides", (LocalSettingsStore store, IApiHostControl apiHost, HttpContext context) =>
        {
            // Nothing to reset: do not stop the host (and abort in-flight scans) for a no-op.
            if (!store.SidecarExists)
            {
                return Results.Ok(new SettingsUpdateResponseDto(false, "No overrides present; nothing to reset."));
            }

            Result deleteResult = store.DeleteOverrides();
            if (deleteResult.IsFailure)
            {
                return Results.Problem(title: "Failed to reset settings", detail: deleteResult.Error, statusCode: StatusCodes.Status500InternalServerError);
            }

            ScheduleHostRestart(context, apiHost);
            return Results.Ok(new SettingsUpdateResponseDto(
                true,
                "Overrides removed; the API host is restarting with the appsettings.json values."));
        })
        .WithName("ResetSettings")
        .WithTags("Settings")
        .Produces<SettingsUpdateResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Registers the in-process host restart to run after the response has been fully sent —
    /// the same post-response pattern the scan endpoint uses for temp-file cleanup. Restarting
    /// earlier would kill the very connection receiving the confirmation.
    /// </summary>
    private static void ScheduleHostRestart(HttpContext context, IApiHostControl apiHost)
    {
        context.Response.OnCompleted(() =>
        {
            apiHost.RequestRestart();
            return Task.CompletedTask;
        });
    }

    [ExcludeFromCodeCoverage(Justification = "Dead code per the Phase 1 audit (C-3): private duplicate of LocalSettingsStore.CreateEmptyOverrides with zero references. Deleted in the dead-code batch.")]
    private static ScannerSettingsOverridesDto CreateEmptyOverrides()
    {
        return new ScannerSettingsOverridesDto(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }
}
