using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for the seam contract of ScannerServiceType.GetScannersListAsync. The method pulls the real
/// ScanController out of the initializer through the internal IScannerInitializerContext
/// interface before it queries any driver, so a test double that does not implement that internal
/// interface fails deterministically at the cast. Real driver enumeration needs live drivers and
/// workers and is covered end-to-end; this test pins the initialization-first ordering and the
/// current failure mode instead.
/// </summary>
public sealed class ScannerServiceGetScannersListTests
{
    [Fact]
    public async Task GetScannersListAsync_FakeInitializerWithoutContext_CurrentBehavior_ThrowsInvalidCastException()
    {
        FakeNonDisposableScannerInitializer initializer = new FakeNonDisposableScannerInitializer();
        ScannerServiceType service = ScannerCoreTestSupport.CreateScannerService(initializer);

        await Assert.ThrowsAsync<InvalidCastException>(() => service.GetScannersListAsync());

        Assert.Equal(1, initializer.InitializeCallCount);
    }
}
