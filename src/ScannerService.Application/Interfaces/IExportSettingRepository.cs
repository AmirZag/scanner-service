using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

internal interface IExportSettingRepository
{
    Task<ExportSettingDto> GetExportSettingAsync();
    Task UpdateExportSettingAsync(ExportSettingDto exportSettingDto);
}
