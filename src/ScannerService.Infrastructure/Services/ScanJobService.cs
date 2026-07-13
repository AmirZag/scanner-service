using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using System.IO.Compression;
using System.Security;

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
        _logger.LogInformation("Scan request received - ProfileId: {ProfileId}, Format: {Format}, ExportPath: {ExportPath}",
            req.ProfileId, req.Format ?? "default", req.ExportPath ?? "default");

        try
        {
            var profile = await _context.Profiles.FindAsync(req.ProfileId);
            if (profile is null)
            {
                _logger.LogWarning("Profile not found - ProfileId: {ProfileId}", req.ProfileId);
                throw new InvalidOperationException($"Profile {req.ProfileId} not found");
            }

            if (string.IsNullOrEmpty(profile.DeviceId))
            {
                _logger.LogWarning("Profile has no device assigned - ProfileId: {ProfileId}, ProfileName: {ProfileName}",
                    req.ProfileId, profile.Name);
                throw new InvalidOperationException("Profile does not have a scanner device assigned");
            }

            var exportSetting = await _exportSettingRepository.GetExportSettingAsync();
            var exportPath = req.ExportPath ?? exportSetting.ExportPath;

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
            }

            // Validate and ensure export path is accessible
            var validationResult = ValidateAndEnsureExportPath(exportPath);
            if (!validationResult.IsValid)
            {
                var errorDuration = DateTime.UtcNow - startTime;
                _logger.LogWarning("Export path validation failed: {ErrorMessage}", validationResult.ErrorMessage);
                return new ScanResultDto(false, null, null, null, validationResult.ErrorMessage, errorDuration);
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
            var scanResult = await _scannerService.ExecuteScanAsync(scanJobConfig);

            if (!scanResult.Success)
            {
                var errorDuration = DateTime.UtcNow - startTime;
                _logger.LogWarning("Scan operation failed: {ErrorMessage}", scanResult.ErrorMessage);
                return new ScanResultDto(false, null, null, null, scanResult.ErrorMessage, errorDuration);
            }

            var files = scanResult.Files ?? throw new InvalidOperationException("Scan result has no files despite success");

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
            _logger.LogInformation("Scan completed successfully - ProfileId: {ProfileId}, ProfileName: {ProfileName}, Files: {FileCount}, Duration: {DurationMs}ms, OutputSize: {OutputSizeBytes}",
                req.ProfileId, profile.Name, files.Count, duration.TotalMilliseconds, fileContent.Length);

            return new ScanResultDto(true, fileContent, fileName, contentType, null, duration);
        }
        catch (Exception ex)
        {
            var errorDuration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Scan failed after {Duration}ms: {Message}", errorDuration.TotalMilliseconds, ex.Message);
            return new ScanResultDto(false, null, null, null, ex.Message, errorDuration);
        }
    }

    private static (bool IsValid, string? ErrorMessage) ValidateAndEnsureExportPath(string exportPath)
    {
        try
        {
            // Check for invalid path characters
            if (exportPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return (false, "Export path contains invalid characters");
            }

            // Ensure the path is absolute
            if (!Path.IsPathRooted(exportPath))
            {
                return (false, "Export path must be an absolute path");
            }

            // Check if path exists or can be created
            if (!Directory.Exists(exportPath))
            {
                try
                {
                    Directory.CreateDirectory(exportPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "Cannot create export directory - access denied. Please choose a different location or run as administrator");
                }
                catch (Exception ex)
                {
                    return (false, $"Cannot create export directory: {ex.Message}");
                }
            }

            // Test write permissions by creating a temporary file
            var testFile = Path.Combine(exportPath, $"~scan_test_{Guid.NewGuid()}.tmp");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Cannot write to export directory - access denied. Please choose a different location or run as administrator");
            }
            catch (IOException ioEx)
            {
                return (false, $"Cannot write to export directory: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Cannot access export directory: {ex.Message}");
            }

            return (true, null);
        }
        catch (ArgumentException ex)
        {
            return (false, $"Invalid export path: {ex.Message}");
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
