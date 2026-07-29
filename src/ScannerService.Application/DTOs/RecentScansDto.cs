namespace ScannerService.Application.DTOs;

/// <summary>
/// Represents a single file within a scan group.
/// Contains metadata about an individual scanned image or PDF file.
/// </summary>
/// <param name="Filename">The name of the file including extension</param>
/// <param name="RelativePath">Full file system path to the scanned file</param>
/// <param name="ContentType">MIME type of the file (e.g., "image/jpeg", "application/pdf")</param>
/// <param name="SizeBytes">File size in bytes</param>
/// <param name="CreatedAt">UTC timestamp when the file was created</param>
public record ScanFileDto(
    string Filename,
    string RelativePath,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt
);

/// <summary>
/// Represents a group of files from the same scan job.
/// Files are grouped by their base name (without _{1,2,3...} suffix for multi-page scans).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Grouping Logic:</strong> Files with names like "scan_001_1.jpg", "scan_001_2.jpg"
/// are grouped together under scanId "scan_001". This represents a single scan job
/// that produced multiple pages or files.
/// </para>
/// <para>
/// <strong>Usage Example:</strong> A scan job with 3 pages produces 3 files that are
/// returned as one ScanGroup with fileCount=3.
/// </para>
/// </remarks>
/// <param name="ScanId">Base identifier for the scan group, derived from the filename without page numbering (e.g., "scan_20250129_143022")</param>
/// <param name="Timestamp">Earliest creation time among all files in this group, representing when the scan occurred</param>
/// <param name="Format">File format inferred from extension: pdf, jpg, png, tiff, or bmp</param>
/// <param name="FileCount">Total number of files in this scan group (1 for single-page, >1 for multi-page scans)</param>
/// <param name="Files">List of all files in this scan group, sorted by filename</param>
public record ScanGroupDto(
    string ScanId,
    DateTime Timestamp,
    string Format,
    int FileCount,
    List<ScanFileDto> Files
);

/// <summary>
/// Response wrapper for the GET /api/recent-scans/{count} endpoint.
/// Contains the most recent scan groups from the export directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Behavior:</strong>
/// <list type="bullet">
/// <item>Scans the export directory recursively (max depth: 3 levels)</item>
/// <item>Finds all supported image/PDF files (pdf, jpg, jpeg, png, tiff, tif, bmp)</item>
/// <item>Groups files by base name to identify multi-page scans</item>
/// <item>Returns results sorted by timestamp (newest first)</item>
/// <item>Requested count acts as a maximum - if fewer scans exist, all are returned</item>
/// </list>
/// </para>
/// <para>
/// <strong>Frontend Usage:</strong> Display scan previews in a modal or grid.
/// Use the <c>RelativePath</c> to construct full image URLs for display.
/// </para>
/// </remarks>
/// <param name="TotalGroups">Total number of scan groups found in the export directory</param>
/// <param name="RequestedCount">The count parameter value from the request URL (e.g., 5 for /api/recent-scans/5)</param>
/// <param name="Groups">Scan groups sorted by timestamp descending (newest first), limited to RequestedCount</param>
public record RecentScansResponseDto(
    int TotalGroups,
    int RequestedCount,
    List<ScanGroupDto> Groups
);
