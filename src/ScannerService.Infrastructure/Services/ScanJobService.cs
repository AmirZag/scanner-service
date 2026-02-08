using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using System.IO.Compression;

namespace ScannerService.Infrastructure.Services;

public class ScanJobService : IScanJobService
{
    private readonly Context _context;
    private readonly IScannerService _scannerService;
    private readonly IExportSettingRepository _exportSettingRepository;
    private readonly ILogger<ScanJobService> _logger;

    public ScanJobService(
        Context context,
        IScannerService scannerService,
        IExportSettingRepository exportSettingRepository,
        ILogger<ScanJobService> logger)
    {
        _context = context;
        _scannerService = scannerService;
        _exportSettingRepository = exportSettingRepository;
        _logger = logger;
    }

    public async Task<ScanResultDto> StartScanJobAsync(ScanRequestDto req)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Scan request received for profile {ProfileId}", req.ProfileId);

        try
        {
            var profile = await _context.Profiles.FindAsync(req.ProfileId);
            if (profile is null)
            {
                _logger.LogWarning("Profile {ProfileId} not found", req.ProfileId);
                throw new InvalidOperationException($"Profile {req.ProfileId} not found");
            }

            if (string.IsNullOrEmpty(profile.DeviceId))
            {
                _logger.LogWarning("Profile {ProfileId} has no device assigned", req.ProfileId);
                throw new InvalidOperationException("Profile does not have a scanner device assigned");
            }

            var exportSetting = await _exportSettingRepository.GetExportSettingAsync();
            var exportPath = req.ExportPath ?? exportSetting.ExportPath;

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
                Directory.CreateDirectory(exportPath);
                _logger.LogInformation("Using default output path: {Path}", exportPath);
            }

            var scanJobConfig = new ScanJobConfiguration
            {
                DeviceId = profile.DeviceId,
                PaperSource = profile.PaperSource,
                BitDepth = profile.BitDepth,
                Resolution = profile.Resolution,
                Brightness = profile.Brightness,
                Contrast = profile.Contrast,
                ImageQuality = profile.ImageQuality,
                Format = req.Format ?? exportSetting.Format,
                ExportPath = exportPath,
                FileName = exportSetting.FileName
            };

            _logger.LogInformation("Starting scan job profile '{ProfileName}'", profile.Name);
            var files = await _scannerService.ExecuteScanAsync(scanJobConfig);

            byte[] fileContent;
            string fileName;
            string contentType;

            if (files.Count == 1)
            {
                var filePath = files[0];
                fileContent = await File.ReadAllBytesAsync(filePath);
                fileName = Path.GetFileName(filePath);
                contentType = GetContentType(filePath);
            }
            else
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.zip");
                ZipFile.CreateFromDirectory(
                    Path.GetDirectoryName(files[0])!,
                    zipPath,
                    CompressionLevel.Optimal,
                    false);

                fileContent = await File.ReadAllBytesAsync(zipPath);
                File.Delete(zipPath);
                fileName = $"scanned_documents_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                contentType = "application/zip";
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Scan completed successfully in {Duration}ms", duration.TotalMilliseconds);

            return new ScanResultDto(true, fileContent, fileName, contentType, null, duration);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Scan failed after {Duration}ms: {Message}", duration.TotalMilliseconds, ex.Message);
            return new ScanResultDto(false, null, null, null, ex.Message, duration);
        }
    }

    private static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tiff" or ".tif" => "image/tiff",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }
}
