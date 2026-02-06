namespace ScannerService.Application.DTOs;

internal record ExportSettingDto(
    string Format,
    string ExportPath,
    string FileName
);
