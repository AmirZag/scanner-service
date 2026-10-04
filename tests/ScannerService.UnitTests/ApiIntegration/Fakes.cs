using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Hand-rolled IScannerQueries fake: returns a fixed device list and counts enumeration calls;
/// can be switched to throw to simulate a driver/enumeration failure (health degradation).
/// </summary>
internal sealed class FakeScannerQueries : IScannerQueries
{
    private readonly List<ScannerDto> _scanners;

    public FakeScannerQueries(List<ScannerDto> scanners)
    {
        _scanners = scanners;
    }

    public int CallCount { get; private set; }

    public bool ThrowOnCall { get; set; }

    public Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (ThrowOnCall)
        {
            throw new InvalidOperationException("scanner enumeration failed");
        }

        return Task.FromResult(_scanners.ToList());
    }
}

/// <summary>Hand-rolled IScannerListCache fake that counts cache-clear requests.</summary>
internal sealed class FakeScannerListCache : IScannerListCache
{
    public int ClearCallCount { get; private set; }

    public void ClearScannerListCache()
    {
        ClearCallCount++;
    }
}

/// <summary>
/// Hand-rolled IScannerService fake for the scan pipeline: records the ScanJobConfiguration it
/// was called with and returns either a fixed file list, a Result failure, or a thrown exception.
/// The returned paths must point at real files the test created.
/// </summary>
internal sealed class FakeScannerService : IScannerService
{
    public List<string> FilesToReturn { get; } = new List<string>();

    public ScanJobConfiguration? LastConfiguration { get; private set; }

    public bool FailWithResult { get; set; }

    public bool ThrowOnExecute { get; set; }

    public Task<Result<List<string>>> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration, CancellationToken cancellationToken = default)
    {
        LastConfiguration = scanJobConfiguration;
        if (ThrowOnExecute)
        {
            throw new InvalidOperationException("driver exploded");
        }

        if (FailWithResult)
        {
            return Task.FromResult(Result<List<string>>.Failure("scanner offline"));
        }

        return Task.FromResult(Result<List<string>>.Success(new List<string>(FilesToReturn)));
    }
}
