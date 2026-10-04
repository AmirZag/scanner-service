using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAPS2.Scan;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tests for the platform driver list and the skip rule applied to initialization failures.
/// The test assembly targets net10.0-windows, so the platform-specific list is asserted
/// exactly as the Windows production configuration.
/// </summary>
public sealed class ScannerDriverFactoryTests
{
    [Fact]
    public void GetAvailableDrivers_OnWindows_ReturnsTwainWiaThenEscl()
    {
        Assert.True(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "The test project targets net10.0-windows, so the platform must be Windows.");

        List<Driver> drivers = ScannerDriverFactory.GetAvailableDrivers();

        Assert.Equal(new List<Driver> { Driver.Twain, Driver.Wia, Driver.Escl }, drivers);
    }

    [Theory]
    [InlineData(Driver.Twain, true, true)]
    [InlineData(Driver.Twain, false, false)]
    [InlineData(Driver.Wia, true, false)]
    [InlineData(Driver.Wia, false, false)]
    [InlineData(Driver.Escl, true, false)]
    [InlineData(Driver.Escl, false, false)]
    [InlineData(Driver.Sane, true, false)]
    [InlineData(Driver.Sane, false, false)]
    public void ShouldSkipDriver_SkipsOnlyTwainAfterWorkerFailure(Driver driver, bool twainWorkerFailed, bool expected)
    {
        bool skipped = ScannerDriverFactory.ShouldSkipDriver(driver, twainWorkerFailed);

        Assert.Equal(expected, skipped);
    }
}
