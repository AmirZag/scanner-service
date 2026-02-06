namespace ScannerService.Application.DTOs;

internal record ScanRequestDto(
    int ProfileId,
    string? ExportPath = null,
    string? Format = null
);

internal record ScanResultDto(
    bool Success,
    List<string> Files,
    string? ErrorMessage,
    TimeSpan Duration
);
