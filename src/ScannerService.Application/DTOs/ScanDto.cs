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

public record ScanExecutionResult(bool Success, List<string>? Files = null, string? ErrorMessage = null)
{
    public static ScanExecutionResult Succeed(List<string> files) => new(true, files, null);
    public static ScanExecutionResult Fail(string errorMessage) => new(false, null, errorMessage);
}
