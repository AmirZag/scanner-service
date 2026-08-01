using ScannerService.Application.Common;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

/// <summary>
/// Interface for export settings repository operations.
/// All methods return Result types for consistent error handling.
/// </summary>
public interface IExportSettingRepository
{
    Task<Result<ExportSettingDto>> GetExportSettingAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default);
}
