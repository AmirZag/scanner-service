using System.Threading;
using System.Threading.Tasks;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Test double for IScannerInitializer that also implements IAsyncDisposable, mirroring the real
/// ScannerInitializer. Counts initialization and disposal calls so lifecycle tests can pin how
/// many times ScannerService drives each hook.
/// </summary>
public sealed class FakeDisposableScannerInitializer : IScannerInitializer, IAsyncDisposable
{
    private int _initializeCallCount;
    private int _disposeCallCount;

    public bool IsInitialized => _initializeCallCount > 0;

    public bool TwainWorkerFailed { get; private set; }

    public int InitializeCallCount => _initializeCallCount;

    public int DisposeCallCount => _disposeCallCount;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _initializeCallCount);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCallCount);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Test double for IScannerInitializer that deliberately does NOT implement IAsyncDisposable, so
/// tests can pin how ScannerService treats initializers it cannot dispose itself.
/// </summary>
public sealed class FakeNonDisposableScannerInitializer : IScannerInitializer
{
    private int _initializeCallCount;

    public bool IsInitialized => _initializeCallCount > 0;

    public bool TwainWorkerFailed { get; private set; }

    public int InitializeCallCount => _initializeCallCount;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _initializeCallCount);
        return Task.CompletedTask;
    }
}
