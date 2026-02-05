using ScannerService.Application.DTOs;
using System.Threading.Tasks;

namespace ScannerService.Application.Interfaces;

public interface IExportSettings
{
    Task<ExportSettingsDto> GetExportSettingsAsync();
    Task UpdateExportSettingsAsync(ExportSettingsDto dto);
}