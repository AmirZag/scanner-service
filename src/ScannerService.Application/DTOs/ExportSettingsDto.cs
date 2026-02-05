namespace ScannerService.Application.DTOs;

public record ExportSettingsDto(
    string OutputFormat,
    string OutputDirectory,
    string FileName
);