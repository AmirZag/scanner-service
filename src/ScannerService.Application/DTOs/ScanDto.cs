namespace ScannerService.Application.DTOs;

public record ScanRequestDto(
    int ProfileId,
    string? ExportPath = null,
    string? Format = null
);

public record ScanResultDto(
    bool Success,
    byte[]? FileContent,
    string? FileName,
    string? ContentType,
    string? ErrorMessage,
    TimeSpan Duration
);
