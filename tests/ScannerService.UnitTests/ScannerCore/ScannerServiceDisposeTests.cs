using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for ScannerServiceType.DisposeAsync lifecycle semantics against a hand-rolled initializer
/// double that counts calls: the service must dispose an IAsyncDisposable initializer exactly once
/// per call (audit finding A-9 documents the resulting double dispose with the DI container),
/// must tolerate repeated disposal without throwing, and must skip initializers that are not
/// IAsyncDisposable. Pure in-memory; no files or drivers involved.
/// </summary>
public sealed class ScannerServiceDisposeTests
{
    [Fact]
    public async Task DisposeAsync_DisposableInitializer_DisposesInitializerExactlyOnce()
    {
        FakeDisposableScannerInitializer initializer = new FakeDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await service.DisposeAsync();

        Assert.Equal(1, initializer.DisposeCallCount);
    }

    // KNOWN BUG A-9: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // ScannerServiceType.DisposeAsync has no idempotence guard: every call disposes the initializer
    // again. In production the initializer is disposed once by the service here and once more by
    // the DI container, which is the audit finding; the service itself tolerates the repeated
    // call without throwing.
    [Fact]
    public async Task DisposeAsync_SecondCall_CurrentBehavior_DisposesInitializerAgainWithoutThrow()
    {
        FakeDisposableScannerInitializer initializer = new FakeDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(2, initializer.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeAsync_NonDisposableInitializer_CompletesWithoutThrow()
    {
        FakeNonDisposableScannerInitializer initializer = new FakeNonDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await service.DisposeAsync();

        Assert.False(initializer.IsInitialized);
    }
}
