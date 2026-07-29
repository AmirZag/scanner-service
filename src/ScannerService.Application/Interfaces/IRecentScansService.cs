using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

/// <summary>
/// Service for retrieving recent scan information from the file system.
/// Provides access to scanned documents stored in the export directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Features:</strong>
/// <list type="bullet">
/// <item>Recursively scans export directory (max depth: 3)</item>
/// <item>Filters by supported formats: PDF, JPG, PNG, TIFF, BMP</item>
/// <item>Groups multi-page scans by base filename</item>
/// <item>Returns results sorted by creation time (newest first)</item>
/// </list>
/// </para>
/// <para>
/// <strong>Frontend Integration:</strong>
/// Use this service to populate a "Recent Scans" preview component.
/// The count parameter allows the UI to control how many items to display.
/// </para>
/// </remarks>
public interface IRecentScansService
{
    /// <summary>
    /// Gets the most recent scan groups from the export directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Directory Source:</strong> Uses the ExportPath from ExportSetting,
    /// or defaults to "MyDocuments\Scans" if not configured.
    /// </para>
    /// <para>
    /// <strong>Grouping Behavior:</strong> Files with names following the pattern
    /// "basename_{number}.ext" are grouped together. For example:
    /// <list type="bullet">
    /// <item>scan_001.jpg → single scan</item>
    /// <item>scan_002_1.jpg, scan_002_2.jpg → grouped as one 2-page scan</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Count Parameter:</strong> Acts as a maximum. If 5 is requested but
    /// only 3 scans exist, all 3 are returned. The frontend can adjust this value
    /// based on UI space available.
    /// </para>
    /// </remarks>
    /// <param name="count">Maximum number of scan groups to return (valid range: 1-100)</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>RecentScansResponseDto containing up to 'count' scan groups, sorted by timestamp descending</returns>
    Task<RecentScansResponseDto> GetRecentScansAsync(int count, CancellationToken cancellationToken = default);
}
