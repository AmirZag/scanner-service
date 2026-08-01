using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using System.IO;
using System.Text.RegularExpressions;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Implementation of the recent scans service.
/// Retrieves scanned documents from the file system and groups them for display.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scanning Behavior:</strong>
/// <list type="bullet">
/// <item>Uses export path from ExportSetting (defaults to MyDocuments\Scans)</item>
/// <item>Recursively searches up to 3 directory levels deep</item>
/// <item>Limited to 1000 files maximum for performance</item>
/// <item>Supports: PDF, JPG, JPEG, PNG, TIFF, TIF, BMP formats</item>
/// </list>
/// </para>
/// <para>
/// <strong>Multi-page Scan Detection:</strong>
/// Files with names like "scan_001_1.jpg" and "scan_001_2.jpg" are grouped
/// together as a single scan job with 2 pages.
/// </para>
/// </remarks>
public partial class RecentScansService : IRecentScansService
{
    private readonly Context _context;
    private readonly ILogger<RecentScansService> _logger;

    public RecentScansService(Context context, ILogger<RecentScansService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RecentScansResponseDto> GetRecentScansAsync(int count, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting recent scans - Count: {Count}", count);

        // Get export path from database
        var exportSetting = await _context.ExportSettings.FindAsync(
            [Domain.Common.ApplicationConstants.Database.DefaultExportSettingId],
            cancellationToken);
        var exportPath = exportSetting?.ExportPath;

        // Use default if not set
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
            _logger.LogDebug("Using default export path: {Path}", exportPath);
        }
        else
        {
            _logger.LogDebug("Using configured export path: {Path}", exportPath);
        }

        // Check if directory exists
        if (!Directory.Exists(exportPath))
        {
            _logger.LogWarning("Export directory does not exist: {Path}", exportPath);
            return new RecentScansResponseDto(0, count, []);
        }

        // Recursively find all supported files using optimized enumeration
        var files = await FindFilesOptimizedAsync(exportPath, 0, cancellationToken);

        if (files.Count == 0)
        {
            _logger.LogInformation("No supported files found in: {Path}", exportPath);
            return new RecentScansResponseDto(0, count, []);
        }

        _logger.LogDebug("Found {FileCount} supported files", files.Count);

        // Group files by scan ID (base name)
        var groups = GroupFilesByScanId(files);

        _logger.LogDebug("Grouped into {GroupCount} scan groups", groups.Count);

        // Sort by timestamp (newest first) and take requested count
        var sortedGroups = groups
            .OrderByDescending(g => g.Timestamp)
            .Take(count)
            .ToList();

        _logger.LogInformation("Returning {ReturnedCount} of {TotalCount} scan groups", sortedGroups.Count, groups.Count);

        return new RecentScansResponseDto(groups.Count, count, sortedGroups);
    }

    /// <summary>
    /// Optimized file enumeration using EnumerationOptions for better performance.
    /// </summary>
    private async Task<List<ScanFileRecord>> FindFilesOptimizedAsync(
        string directory,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (currentDepth > Domain.Common.ApplicationConstants.RecentScans.MaxDepth)
        {
            return new List<ScanFileRecord>();
        }

        var files = new List<ScanFileRecord>();
        var maxFiles = Domain.Common.ApplicationConstants.RecentScans.MaxFiles;

        // Use EnumerationOptions for better performance and error handling
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
        };

        try
        {
            // Get files from current directory with optimized enumeration
            var currentDirFiles = await Task.Run(() =>
            {
                var fileList = new List<string>();

                foreach (var filePath in Directory.EnumerateFiles(directory, "*.*", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(filePath);
                    if (Domain.Common.ApplicationConstants.SupportedExtensions.ScanFiles.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        fileList.Add(filePath);
                    }

                    // Stop early if we reach the limit
                    if (fileList.Count >= maxFiles)
                    {
                        break;
                    }
                }

                return fileList;
            }, cancellationToken);

            // Get file info in batch
            foreach (var filePath in currentDirFiles)
            {
                if (files.Count >= maxFiles)
                {
                    _logger.LogWarning("Reached maximum file limit ({MaxFiles})", maxFiles);
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    files.Add(new ScanFileRecord(
                        fileInfo.Name,
                        filePath,
                        fileInfo.Extension,
                        fileInfo.Length,
                        fileInfo.CreationTimeUtc
                    ));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Cannot access file: {FilePath}", filePath);
                }
            }

            // Recursively scan subdirectories if not at max depth
            if (currentDepth < Domain.Common.ApplicationConstants.RecentScans.MaxDepth && files.Count < maxFiles)
            {
                var subdirectories = await GetSubdirectoriesSafeAsync(directory, cancellationToken);

                foreach (var subdirectory in subdirectories)
                {
                    if (files.Count >= maxFiles)
                    {
                        break;
                    }

                    var subFiles = await FindFilesOptimizedAsync(subdirectory, currentDepth + 1, cancellationToken);
                    var remainingSlots = maxFiles - files.Count;
                    files.AddRange(subFiles.Take(remainingSlots));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cannot access directory: {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning directory: {Directory}", directory);
        }

        return files;
    }

    /// <summary>
    /// Gets subdirectories safely with error handling.
    /// </summary>
    private Task<List<string>> GetSubdirectoriesSafeAsync(string directory, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subdirectories = new List<string>();
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
            };

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(directory, "*", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    subdirectories.Add(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Cannot enumerate subdirectories of: {Directory}", directory);
            }

            return subdirectories;
        }, cancellationToken);
    }

    /// <summary>
    /// Groups files by their scan ID to identify multi-page scans.
    /// </summary>
    private List<ScanGroupDto> GroupFilesByScanId(List<ScanFileRecord> files)
    {
        var groups = new Dictionary<string, List<ScanFileRecord>>();

        foreach (var file in files)
        {
            var scanId = ExtractScanId(file.Filename);

            if (!groups.TryGetValue(scanId, out var fileList))
            {
                fileList = new List<ScanFileRecord>();
                groups[scanId] = fileList;
            }
            fileList.Add(file);
        }

        var result = new List<ScanGroupDto>();

        foreach (var (scanId, groupFiles) in groups)
        {
            var format = groupFiles[0].Extension.TrimStart('.').ToLowerInvariant();
            var timestamp = groupFiles.Min(f => f.CreatedAtUtc);

            var fileDtos = groupFiles
                .OrderBy(f => f.Filename)
                .Select(f => new ScanFileDto(
                    f.Filename,
                    f.FullPath,
                    GetContentType(f.Extension),
                    f.SizeBytes,
                    f.CreatedAtUtc
                ))
                .ToList();

            result.Add(new ScanGroupDto(
                scanId,
                timestamp,
                format,
                fileDtos.Count,
                fileDtos
            ));
        }

        return result;
    }

    /// <summary>
    /// Extracts scan ID from filename by removing the page numbering suffix and extension.
    /// </summary>
    private static string ExtractScanId(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        var match = UnderscoreNumberSuffixRegex().Match(nameWithoutExt);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return nameWithoutExt;
    }

    /// <summary>
    /// Gets the MIME content type for a file extension.
    /// </summary>
    private static string GetContentType(string extension)
    {
        return Application.Common.ContentTypes.GetContentType(extension);
    }

    /// <summary>
    /// Matches pattern: name_{number} at the end of the string
    /// </summary>
    [GeneratedRegex(@"^(.+?)_\d+$", RegexOptions.Compiled)]
    private static partial Regex UnderscoreNumberSuffixRegex();

    /// <summary>
    /// Internal record for file tracking
    /// </summary>
    private sealed record ScanFileRecord(
        string Filename,
        string FullPath,
        string Extension,
        long SizeBytes,
        DateTime CreatedAtUtc
    );
}
