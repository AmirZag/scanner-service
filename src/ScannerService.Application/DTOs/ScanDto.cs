using ScannerService.Application.Common;

namespace ScannerService.Application.DTOs;

public sealed record ScanRequestDto(
    int ProfileId,
    string? ExportPath = null,
    string? Format = null
);

public sealed record ScanResultDto(
    bool Success,
    string? FilePath,  // Path to the scanned file (or zip for multi-page scans)
    string? FileName,
    string? ContentType,
    string? ErrorMessage,
    TimeSpan Duration)
{
    /// <summary>
    /// Creates a successful ScanResultDto.
    /// </summary>
    public static ScanResultDto Successful(string filePath, string fileName, string contentType, TimeSpan duration) =>
        new(true, filePath, fileName, contentType, null, duration);

    /// <summary>
    /// Creates a failed ScanResultDto.
    /// </summary>
    public static ScanResultDto Failed(string errorMessage, TimeSpan duration) =>
        new(false, null, null, null, errorMessage, duration);
}
