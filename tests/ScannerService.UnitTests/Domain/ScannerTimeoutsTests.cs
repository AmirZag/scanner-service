using System;
using ScannerService.Domain.Common;
using Xunit;

namespace ScannerService.UnitTests.Domain;

public class ScannerTimeoutsTests
{
    [Fact]
    public void FreshInstance_DefaultsToScannerConstants_AndExposesComputedTimeSpans()
    {
        ScannerTimeouts timeouts = new ScannerTimeouts();

        Assert.Equal(ScannerConstants.Timeouts.DefaultDriverTimeoutMs, timeouts.DriverTimeoutMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultEsclSearchTimeoutMs, timeouts.EsclSearchTimeoutMs);
        Assert.Equal(ScannerConstants.Timeouts.EsclSearchMarginMs, timeouts.EsclSearchMarginMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultDriverCooldownMs, timeouts.DriverCooldownMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultDriverCooldownMaxMs, timeouts.DriverCooldownMaxMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultScanQueueTimeoutMs, timeouts.ScanQueueTimeoutMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultScanOverallTimeoutMs, timeouts.ScanOverallTimeoutMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultScanNoProgressTimeoutMs, timeouts.ScanNoProgressTimeoutMs);
        Assert.Equal(ScannerConstants.Timeouts.DefaultShutdownTimeoutMs, timeouts.ShutdownTimeoutMs);

        Assert.Equal(TimeSpan.FromMilliseconds(15000), timeouts.DriverTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(8000 + 2000), timeouts.EsclDeviceSearchBudget);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), timeouts.ScanQueueTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(600000), timeouts.ScanOverallTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(120000), timeouts.ScanNoProgressTimeout);
    }

    [Fact]
    public void InitOnlyProperties_RecomputeTheDerivedTimeSpans()
    {
        ScannerTimeouts timeouts = new ScannerTimeouts
        {
            DriverTimeoutMs = 1000,
            EsclSearchTimeoutMs = 600,
            EsclSearchMarginMs = 400,
            ScanQueueTimeoutMs = 2500,
            ScanOverallTimeoutMs = 30000,
            ScanNoProgressTimeoutMs = 15000
        };

        Assert.Equal(TimeSpan.FromSeconds(1), timeouts.DriverTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), timeouts.EsclDeviceSearchBudget);
        Assert.Equal(TimeSpan.FromSeconds(2.5), timeouts.ScanQueueTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), timeouts.ScanOverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), timeouts.ScanNoProgressTimeout);
    }
}
