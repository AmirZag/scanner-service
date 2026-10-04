using System;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using ScannerService.UnitTests.Sidecar;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

/// <summary>
/// ApiHostControl is a thin read-only wrapper over WebApiHostService; the tests pin the
/// forwarding of every host fact (actual/configured port, URL host) and of the restart trigger.
/// The wrapped host is only constructed, never started, so no port or sidecar state is touched;
/// the BinState collection covers the store constructor's shared-bin temp-file sweep.
/// </summary>
[Collection("BinState")]
public class ApiHostControlForwardingTests : IDisposable
{
    public ApiHostControlForwardingTests()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public void Properties_ForwardEveryHostFactToTheWrappedHost()
    {
        ScannerServiceConfiguration configuration = new ScannerServiceConfiguration
        {
            ApiPort = 50555,
            ApiHost = "localhost"
        };
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        var control = new ApiHostControl(host);

        Assert.Equal(host.ActualPort, control.ActualPort);
        Assert.Equal(host.ConfiguredPort, control.ConfiguredPort);
        Assert.Equal(host.LocalUrlHost, control.BindHost);
        Assert.Equal(50555, control.ActualPort);
        Assert.Equal(50555, control.ConfiguredPort);
        Assert.Equal("localhost", control.BindHost);
    }

    [Fact]
    public void BindHost_WithSpecificAddressConfig_ForwardsTheHostUrlHost()
    {
        ScannerServiceConfiguration configuration = new ScannerServiceConfiguration
        {
            ApiPort = 50556,
            ApiHost = "192.168.50.50"
        };
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        var control = new ApiHostControl(host);

        Assert.Equal(host.LocalUrlHost, control.BindHost);
        Assert.Equal("192.168.50.50", control.BindHost);
    }

    [Fact]
    public void RequestRestart_RaisesTheWrappedHostRestartEvent()
    {
        ScannerServiceConfiguration configuration = new ScannerServiceConfiguration
        {
            ApiPort = 50557,
            ApiHost = "localhost"
        };
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        var control = new ApiHostControl(host);
        int restartCount = 0;
        host.RestartRequested += (_, _) => restartCount++;

        control.RequestRestart();

        Assert.Equal(1, restartCount);
    }
}
