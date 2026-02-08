namespace ScannerService.Application.DTOs;

public record ExportSettingDto(
    string Format,
    string ExportPath,
    string FileName
);
