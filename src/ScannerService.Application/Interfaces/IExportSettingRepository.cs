using System.Threading;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IExportSettingRepository
{
    Task<ExportSettingDto> GetExportSettingAsync(CancellationToken cancellationToken = default);
    Task UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default);
}
