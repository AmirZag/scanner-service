using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IExportSettingRepository
{
    Task<ExportSettingDto> GetExportSettingAsync();
    Task UpdateExportSettingAsync(ExportSettingDto exportSettingDto);
}
