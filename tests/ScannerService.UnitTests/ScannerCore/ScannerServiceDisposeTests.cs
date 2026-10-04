using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for ScannerServiceType.DisposeAsync lifecycle semantics against a hand-rolled initializer
/// double that counts calls. FIXED (Phase 2 Batch 3, audit A-9): the service must NOT dispose the
/// initializer at all - the DI container registers it as a container-activated singleton and owns
/// its disposal; the service used to dispose it too, making every shutdown a double dispose. It
/// must also tolerate repeated disposal without throwing, and must skip initializers that are not
/// IAsyncDisposable. Pure in-memory; no files or drivers involved.
/// </summary>
public sealed class ScannerServiceDisposeTests
{
    [Fact]
    public async Task DisposeAsync_LeavesInitializerDisposalToTheContainer()
    {
        FakeDisposableScannerInitializer initializer = new FakeDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await service.DisposeAsync();

        Assert.Equal(0, initializer.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeAsync_SecondCall_CompletesWithoutThrow()
    {
        FakeDisposableScannerInitializer initializer = new FakeDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(0, initializer.DisposeCallCount);
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
