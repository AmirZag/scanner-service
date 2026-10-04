using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Hand-rolled <see cref="IScannerQueries"/> fake whose enumeration blocks on a gate so tests
/// can hold an in-flight enumeration open, then release it. Exposes a signal that resolves as
/// soon as the inner enumeration has been entered.
/// </summary>
internal sealed class GatedScannerQueriesFake : IScannerQueries
{
    private readonly TaskCompletionSource<bool> _enumerationStarted =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<List<ScannerDto>> _enumerationCompletion =
        new TaskCompletionSource<List<ScannerDto>>(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public Task WaitUntilEnumeratingAsync()
    {
        return _enumerationStarted.Task;
    }

    public void Release()
    {
        _enumerationCompletion.TrySetResult(new List<ScannerDto>
        {
            new ScannerDto("gated-scanner", "Gated Scanner", "Escl")
        });
    }

    public Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        _enumerationStarted.TrySetResult(true);
        return _enumerationCompletion.Task;
    }
}
