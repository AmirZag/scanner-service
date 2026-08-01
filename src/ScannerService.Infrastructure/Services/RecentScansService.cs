using Microsoft.Extensions.Logging;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
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
        var exportSetting = await _context.ExportSettings.FindAsync([Domain.Common.ApplicationConstants.Database.DefaultExportSettingId], cancellationToken);
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

        // Recursively find all supported files
        var files = FindFilesRecursively(exportPath, 0, cancellationToken);

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
    /// Recursively finds all supported files in the directory tree.
    /// </summary>
    /// <remarks>
    /// Searches for image and PDF files up to MaxDepth levels deep.
    /// Enforces MaxFiles limit to prevent excessive filesystem scanning.
    /// </remarks>
    /// <param name="directory">Directory to scan</param>
    /// <param name="currentDepth">Current recursion depth (0 for root)</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>List of found files with metadata</returns>
    private List<ScanFileRecord> FindFilesRecursively(string directory, int currentDepth, CancellationToken cancellationToken)
    {
        var files = new List<ScanFileRecord>();
        cancellationToken.ThrowIfCancellationRequested();

        if (currentDepth > Domain.Common.ApplicationConstants.RecentScans.MaxDepth)
        {
            return files;
        }

        try
        {
            // Single enumeration of all files, filtered by supported extensions
            var allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(filePath =>
                {
                    var ext = Path.GetExtension(filePath);
                    return Domain.Common.ApplicationConstants.SupportedExtensions.ScanFiles.Contains(ext, StringComparer.OrdinalIgnoreCase);
                });

            foreach (var filePath in allFiles)
            {
                if (files.Count >= Domain.Common.ApplicationConstants.RecentScans.MaxFiles)
                {
                    _logger.LogWarning("Reached maximum file limit ({MaxFiles})", Domain.Common.ApplicationConstants.RecentScans.MaxFiles);
                    return files;
                }

                var fileInfo = new FileInfo(filePath);
                files.Add(new ScanFileRecord(
                    fileInfo.Name,
                    filePath,
                    fileInfo.Extension,
                    fileInfo.Length,
                    fileInfo.CreationTimeUtc
                ));
            }

            // Recursively scan subdirectories
            if (currentDepth < Domain.Common.ApplicationConstants.RecentScans.MaxDepth)
            {
                var subdirectories = Directory.EnumerateDirectories(directory);
                foreach (var subdirectory in subdirectories)
                {
                    if (files.Count >= Domain.Common.ApplicationConstants.RecentScans.MaxFiles)
                    {
                        return files;
                    }

                    var subFiles = FindFilesRecursively(subdirectory, currentDepth + 1, cancellationToken);
                    files.AddRange(subFiles);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
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
    /// Groups files by their scan ID to identify multi-page scans.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Files are grouped by their base name (without page numbering suffix).
    /// This allows multi-page scans to be displayed as a single group.
    /// </para>
    /// <para>
    /// <strong>Examples:</strong>
    /// <list type="bullet">
    /// <item>"scan_001.jpg" → group with 1 file</item>
    /// <item>"scan_002_1.jpg", "scan_002_2.jpg" → group "scan_002" with 2 files</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="files">List of files to group</param>
    /// <returns>List of scan groups with metadata</returns>
    private List<ScanGroupDto> GroupFilesByScanId(List<ScanFileRecord> files)
    {
        var groups = new Dictionary<string, List<ScanFileRecord>>();

        foreach (var file in files)
        {
            // Extract scan ID (base name without _{number} suffix and extension)
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
            // Get format from first file's extension
            var format = groupFiles[0].Extension.TrimStart('.').ToLowerInvariant();

            // Find earliest timestamp in the group
            var timestamp = groupFiles.Min(f => f.CreatedAtUtc);

            // Convert to DTOs
            var fileDtos = groupFiles
                .OrderBy(f => f.Filename) // Sort files by name for consistent ordering
                .Select(f => new ScanFileDto(
                    f.Filename,
                    f.FullPath, // Could be converted to relative path if needed
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
    /// <remarks>
    /// <para>
    /// Used to group multi-page scans together. Removes trailing _{number} patterns
    /// that indicate page numbers in multi-page scans.
    /// </para>
    /// <para>
    /// <strong>Examples:</strong>
    /// <list type="bullet">
    /// <item>"scan_001.pdf" → "scan_001" (single page)</item>
    /// <item>"scan_002_1.jpg" → "scan_002" (page 1)</item>
    /// <item>"scan_002_2.jpg" → "scan_002" (page 2, groups with page 1)</item>
    /// <item>"document.pdf" → "document" (no page numbering)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="filename">Filename to process (can include extension)</param>
    /// <returns>Base name without page numbering or extension</returns>
    private static string ExtractScanId(string filename)
    {
        // Remove extension
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

        // Check for _{number} pattern (e.g., scan_001_1, scan_001_2)
        var match = UnderscoreNumberSuffixRegex().Match(nameWithoutExt);
        if (match.Success)
        {
            // Return the part before _{number}
            return match.Groups[1].Value;
        }

        return nameWithoutExt;
    }

    /// <summary>
    /// Gets the MIME content type for a file extension.
    /// </summary>
    /// <remarks>
    /// Maps file extensions to their corresponding MIME types for proper
    /// HTTP content-type headers in the API response.
    /// </remarks>
    /// <param name="extension">File extension including the dot (e.g., ".jpg", ".pdf")</param>
    /// <returns>MIME content type string</returns>
    private static string GetContentType(string extension)
    {
        return ContentTypes.GetContentType(extension);
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
