using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Inert fallback implementations for the endpoint map's service dependencies. Their methods
/// throw so a test that unexpectedly reaches one fails with an obvious message instead of
/// silently asserting against fake data.
/// </summary>
internal sealed class FakeScanJobService : IScanJobService
{
    public Task<Result<ScanResultDto>> StartScanJobAsync(ScanRequestDto req, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IScanJobService.");
    }

    public void CleanupOldTempFiles(TimeSpan maxAge)
    {
    }

    public Result CleanupTempFile(string filePath)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IScanJobService.");
    }
}

internal sealed class FakeRecentScansService : IRecentScansService
{
    public Task<RecentScansResponseDto> GetRecentScansAsync(int count, CancellationToken cancellationToken = default)
    {
        RecentScansResponseDto response = new RecentScansResponseDto(0, count, new List<ScanGroupDto>());
        return Task.FromResult(response);
    }
}

internal sealed class FakeProfileRepository : IProfileRepository
{
    public Task<List<ProfileDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<ProfileDto>());
    }

    public Task<ProfileDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IProfileRepository.");
    }

    public Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IProfileRepository.");
    }

    public Task<Result<ProfileDto>> UpdateAsync(int id, UpdateProfileDto updateProfileDto, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IProfileRepository.");
    }

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IProfileRepository.");
    }
}

internal sealed class FakeExportSettingRepository : IExportSettingRepository
{
    public Task<Result<ExportSettingDto>> GetExportSettingAsync(CancellationToken cancellationToken = default)
    {
        ExportSettingDto dto = new ExportSettingDto("PDF", string.Empty, "scan_{datetime}");
        return Task.FromResult(Result<ExportSettingDto>.Success(dto));
    }

    public Task<Result> UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Default endpoint fallback reached; the test should register a real IExportSettingRepository.");
    }
}
