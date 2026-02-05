using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ScannerService.Infrastructure.Services;

public class ScanService : IScanService
{
    private readonly IScannerDeviceService _scannerDeviceService;
    private readonly IExportSettings _exportSettings;
    private readonly ScannerDbContext _context;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        IScannerDeviceService scannerDeviceService,
        IExportSettings exportSettings,
        ScannerDbContext context,
        ILogger<ScanService> logger)
    {
        _scannerDeviceService = scannerDeviceService;
        _exportSettings = exportSettings;
        _context = context;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(ScanRequest request)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Scan request received for profile {ProfileId}", request.ProfileId);

        try
        {
            var profile = await _context.Profiles.FindAsync(request.ProfileId);
            if (profile == null)
            {
                _logger.LogWarning("Profile {ProfileId} not found", request.ProfileId);
                throw new InvalidOperationException($"Profile {request.ProfileId} not found");
            }

            if (string.IsNullOrEmpty(profile.DeviceId))
            {
                _logger.LogWarning("Profile {ProfileId} has no device assigned", request.ProfileId);
                throw new InvalidOperationException("Profile does not have a scanner device assigned");
            }

            var settings = await _exportSettings.GetExportSettingsAsync();

            var outputPath = request.OutputPath ?? settings.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
                Directory.CreateDirectory(outputPath);
                _logger.LogInformation("Using default output path: {Path}", outputPath);
            }

            var scanRequest = new ScanJobConfiguration
            {
                DeviceId = profile.DeviceId,
                PaperSource = profile.PaperSource,
                BitDepth = profile.BitDepth,
                Resolution = profile.Resolution,
                Brightness = profile.Brightness,
                Contrast = profile.Contrast,
                ImageQuality = profile.ImageQuality,
                OutputFormat = request.OutputFormat ?? settings.OutputFormat,
                OutputPath = outputPath,
                FileName = settings.FileName
            };

            _logger.LogInformation("Starting scan with profile '{ProfileName}'", profile.Name);
            var files = await _scannerDeviceService.DeviceScanAsync(scanRequest);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Scan completed successfully in {Duration}ms. {Count} files created",
                duration.TotalMilliseconds, files.Count);

            return new ScanResult(true, files, null, duration);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Scan failed after {Duration}ms: {Message}", duration.TotalMilliseconds, ex.Message);
            return new ScanResult(false, new List<string>(), ex.Message, duration);
        }
    }
}