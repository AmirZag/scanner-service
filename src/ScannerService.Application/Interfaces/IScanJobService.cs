using ScannerService.Application.Common;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

/// <summary>
/// Interface for managing scan jobs.
/// All methods return Result types for consistent error handling.
/// </summary>
public interface IScanJobService
{
    /// <summary>
    /// Starts a scan job with the specified request parameters.
    /// Returns Result{ScanResultDto} containing the scan result or error information.
    /// </summary>
    Task<Result<ScanResultDto>> StartScanJobAsync(ScanRequestDto req, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old temporary files created during scan operations.
    /// </summary>
    void CleanupOldTempFiles(TimeSpan maxAge);

    /// <summary>
    /// Cleans up a specific temporary file immediately.
    /// Returns Result indicating success or failure.
    /// </summary>
    Result CleanupTempFile(string filePath);
}
