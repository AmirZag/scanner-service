namespace ScannerService.Application.DTOs;

public record ScanRequestDto(
    int ProfileId,
    string? ExportPath = null,
    string? Format = null
);

public record ScanResultDto(
    bool Success,
    List<string> Files,
    string? ErrorMessage,
    TimeSpan Duration
);
