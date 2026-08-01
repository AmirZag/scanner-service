using Microsoft.Extensions.Logging;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using System.IO.Compression;
using System.Security;
using System.Runtime.InteropServices;

namespace ScannerService.Infrastructure.Services;

public class ScanJobService : IScanJobService
{
    private readonly Context _context;
    private readonly IScannerService _scannerService;
    private readonly IExportSettingRepository _exportSettingRepository;
    private readonly ILogger<ScanJobService> _logger;
    private readonly HashSet<string> _tempFilesToDelete = new();

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

    /// <summary>
    /// Cleans up old temporary files that were created for multi-page scans.
    /// Should be called periodically (e.g., on application shutdown or via a timer).
    /// </summary>
    public void CleanupOldTempFiles(TimeSpan maxAge)
    {
        lock (_tempFilesToDelete)
        {
            var now = DateTime.UtcNow;
            var filesToDelete = new List<string>();

            foreach (var filePath in _tempFilesToDelete)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (now - fileInfo.CreationTimeUtc > maxAge)
                        {
                            filesToDelete.Add(filePath);
                        }
                    }
                    else
                    {
                        // File doesn't exist, remove from tracking
                        filesToDelete.Add(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking temp file: {FilePath}", filePath);
                    filesToDelete.Add(filePath);
                }
            }

            // Delete the files and remove from tracking
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger.LogDebug("Cleaned up old temporary file: {FilePath}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {FilePath}", filePath);
                }
                _tempFilesToDelete.Remove(filePath);
            }
        }
    }

    /// <summary>
    /// Cleans up a specific temporary file immediately.
    /// Intended to be called after the file has been sent to the client.
    /// </summary>
    public void CleanupTempFile(string filePath)
    {
        if (_tempFilesToDelete.Remove(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogDebug("Cleaned up temporary file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temporary file: {FilePath}", filePath);
            }
        }
    }

    public async Task<ScanResultDto> StartScanJobAsync(ScanRequestDto req, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Scan request received - ProfileId: {ProfileId}, Format: {Format}, ExportPath: {ExportPath}",
            req.ProfileId, req.Format ?? "default", req.ExportPath ?? "default");

        try
        {
            var profile = await _context.Profiles.FindAsync([req.ProfileId], cancellationToken);
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

            var exportSetting = await _exportSettingRepository.GetExportSettingAsync(cancellationToken);
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
            var scanResult = await _scannerService.ExecuteScanAsync(scanJobConfig, cancellationToken);

            if (!scanResult.Success)
            {
                var errorDuration = DateTime.UtcNow - startTime;
                _logger.LogWarning("Scan operation failed: {ErrorMessage}", scanResult.ErrorMessage);
                return new ScanResultDto(false, null, null, null, scanResult.ErrorMessage, errorDuration);
            }

            var files = scanResult.Files ?? throw new InvalidOperationException("Scan result has no files despite success");

            string filePath;
            string fileName;
            string contentType;

            if (files.Count == 1)
            {
                filePath = files[0];
                fileName = Path.GetFileName(filePath);
                contentType = ContentTypes.GetContentTypeFromPath(filePath);
            }
            else
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.zip");
                ZipFile.CreateFromDirectory(
                    Path.GetDirectoryName(files[0])!,
                    zipPath,
                    CompressionLevel.Optimal,
                    false);

                // Track for cleanup
                lock (_tempFilesToDelete)
                {
                    _tempFilesToDelete.Add(zipPath);
                }

                filePath = zipPath;
                fileName = $"scanned_documents_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                contentType = ContentTypes.GetContentType(".zip");
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Scan completed successfully - ProfileId: {ProfileId}, ProfileName: {ProfileName}, Files: {FileCount}, Duration: {DurationMs}ms, OutputPath: {OutputPath}",
                req.ProfileId, profile.Name, files.Count, duration.TotalMilliseconds, filePath);

            return new ScanResultDto(true, filePath, fileName, contentType, null, duration);
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
}
