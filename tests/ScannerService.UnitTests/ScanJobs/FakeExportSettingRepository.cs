using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Hand-rolled IExportSettingRepository fake that returns one fixed read outcome. Only the read
/// path is consumed by ScanJobService tests (the real repository never fails on a missing row -
/// it creates a default - so the failure branch of the service needs this fake).
/// </summary>
internal sealed class FakeExportSettingRepository : IExportSettingRepository
{
    public FakeExportSettingRepository(Result<ExportSettingDto> resultToReturn)
    {
        ResultToReturn = resultToReturn;
    }

    public Result<ExportSettingDto> ResultToReturn { get; }

    public Task<Result<ExportSettingDto>> GetExportSettingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResultToReturn);
    }

    public Task<Result> UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure("Update is not exercised by ScanJobService tests"));
    }
}
