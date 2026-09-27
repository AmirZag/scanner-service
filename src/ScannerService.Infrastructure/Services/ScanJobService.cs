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

    // The service is registered scoped, but temp files must be tracked process-wide: cleanup runs from
    // a different scope at shutdown, and files created in request scopes would otherwise be invisible.
    private static readonly HashSet<string> TempFilesToDelete = new();

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
        lock (TempFilesToDelete)
        {
            var now = DateTime.UtcNow;
            var filesToDelete = new List<string>();

            foreach (var filePath in TempFilesToDelete)
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

            // Delete the files, untracking only what was actually removed so a file that could not be
            // deleted (e.g. still being streamed) is retried by a later sweep instead of leaking.
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger.LogDebug("Cleaned up old temporary file: {FilePath}", filePath);
                    }

                    TempFilesToDelete.Remove(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {FilePath}", filePath);
                }
            }
        }
    }

    /// <summary>
    /// Cleans up a specific temporary file immediately.
    /// Intended to be called after the file has been sent to the client.
    /// Returns Result indicating success or failure.
    /// </summary>
    public Result CleanupTempFile(string filePath)
    {
        bool tracked;
        lock (TempFilesToDelete)
        {
            tracked = TempFilesToDelete.Remove(filePath);
        }

        if (!tracked)
        {
            return Result.Failure("File not found in temp files tracking");
        }

        try
        {
            File.Delete(filePath);
            _logger.LogDebug("Cleaned up temporary file: {FilePath}", filePath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            // Keep the entry so a later sweep (e.g. at shutdown) retries; untracking here would
            // leak the file for the rest of the process lifetime.
            lock (TempFilesToDelete)
            {
                TempFilesToDelete.Add(filePath);
            }

            _logger.LogWarning(ex, "Failed to clean up temporary file: {FilePath}", filePath);
            return Result.Failure($"Failed to clean up temporary file: {ex.Message}");
        }
    }

    public async Task<Result<ScanResultDto>> StartScanJobAsync(ScanRequestDto req, CancellationToken cancellationToken = default)
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
                return Result<ScanResultDto>.Failure($"Profile {req.ProfileId} not found");
            }

            if (string.IsNullOrEmpty(profile.DeviceId))
            {
                _logger.LogWarning("Profile has no device assigned - ProfileId: {ProfileId}, ProfileName: {ProfileName}",
                    req.ProfileId, profile.Name);
                return Result<ScanResultDto>.Failure("Profile does not have a scanner device assigned");
            }

            var exportSettingResult = await _exportSettingRepository.GetExportSettingAsync(cancellationToken);
            if (exportSettingResult.IsFailure)
            {
                _logger.LogWarning("Failed to retrieve export settings: {ErrorMessage}", exportSettingResult.Error);
                return Result<ScanResultDto>.Failure($"Failed to retrieve export settings: {exportSettingResult.Error}");
            }

            var exportSetting = exportSettingResult.Value!;
            var exportPath = req.ExportPath ?? exportSetting.ExportPath;

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
            }

            // Validate and ensure export path is accessible
            var validationResult = ValidateAndEnsureExportPath(exportPath);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Export path validation failed: {ErrorMessage}", validationResult.ErrorMessage);
                return Result<ScanResultDto>.Failure(validationResult.ErrorMessage ?? "Export path validation failed");
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

            if (scanResult.IsFailure)
            {
                _logger.LogWarning("Scan operation failed: {ErrorMessage}", scanResult.Error);
                return Result<ScanResultDto>.Failure(scanResult.Error ?? "Scan operation failed");
            }

            var files = scanResult.Value ?? throw new InvalidOperationException("Scan result has no files despite success");

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

                // Zip only this scan's files; the export directory may hold months of earlier scans
                // and zipping it wholesale would leak them into every multi-page response.
                await using (var zipStream = new FileStream(zipPath, FileMode.CreateNew))
                await using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        await archive.CreateEntryFromFileAsync(file, Path.GetFileName(file), CompressionLevel.Optimal, cancellationToken);
                    }
                }

                // Track for cleanup
                lock (TempFilesToDelete)
                {
                    TempFilesToDelete.Add(zipPath);
                }

                filePath = zipPath;
                fileName = $"scanned_documents_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                contentType = ContentTypes.GetContentType(".zip");
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Scan completed successfully - ProfileId: {ProfileId}, ProfileName: {ProfileName}, Files: {FileCount}, Duration: {DurationMs}ms, OutputPath: {OutputPath}",
                req.ProfileId, profile.Name, files.Count, duration.TotalMilliseconds, filePath);

            return Result<ScanResultDto>.Success(ScanResultDto.Successful(filePath, fileName, contentType, duration));
        }
        catch (Exception ex)
        {
            var errorDuration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Scan failed after {Duration}ms: {Message}", errorDuration.TotalMilliseconds, ex.Message);
            return Result<ScanResultDto>.Failure(ex.Message);
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
