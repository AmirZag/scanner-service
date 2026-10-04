using System;
using System.IO;
using System.Threading.Tasks;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Sidecar;

[Collection("BinState")]
public class LocalSettingsStoreLoadEffectiveTests : IDisposable
{
    public LocalSettingsStoreLoadEffectiveTests()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public void LoadEffectiveConfiguration_WithoutSidecarFile_ReturnsBaseConfiguration()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsSuccess);
        Assert.NotNull(loadResult.Value);
        ScannerServiceConfiguration effective = loadResult.Value;
        Assert.Equal(50123, effective.ApiPort);
        Assert.Equal("127.0.0.1", effective.ApiHost);
        Assert.Equal(7000, effective.StatusCheckInterval);
        Assert.Equal(3000, effective.HttpTimeout);
        Assert.Equal(1500, effective.StartupDelay);
        Assert.Equal(20000, effective.DriverTimeoutMs);
        Assert.Equal(8000, effective.EsclSearchTimeoutMs);
        Assert.Equal(2500, effective.EsclSearchMarginMs);
        Assert.Equal(45000, effective.DriverCooldownMs);
        Assert.Equal(300000, effective.DriverCooldownMaxMs);
        Assert.Equal(4000, effective.ScanQueueTimeoutMs);
        Assert.Equal(300000, effective.ScanOverallTimeoutMs);
        Assert.Equal(90000, effective.ScanNoProgressTimeoutMs);
        Assert.Equal(8000, effective.ShutdownTimeoutMs);
        Assert.Equal(30, effective.ScannersRequestTimeoutSeconds);
        Assert.Equal(400, effective.ScanRequestTimeoutSeconds);
        EsclManualDeviceConfiguration baseDevice = Assert.Single(effective.EsclManualDevices);
        Assert.Equal(SidecarTestData.BaseDeviceAddress, baseDevice.Address);
        Assert.Null(baseDevice.Name);
    }

    [Fact]
    public async Task LoadEffectiveConfiguration_WithAllFieldsOverridden_AppliesOverrideValues()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateFullOverrides());
        Assert.True(writeResult.IsSuccess);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsSuccess);
        Assert.NotNull(loadResult.Value);
        ScannerServiceConfiguration effective = loadResult.Value;
        Assert.Equal(9000, effective.StatusCheckInterval);
        Assert.Equal(4000, effective.HttpTimeout);
        Assert.Equal(2500, effective.StartupDelay);
        Assert.Equal(22000, effective.DriverTimeoutMs);
        Assert.Equal(8500, effective.EsclSearchTimeoutMs);
        Assert.Equal(2600, effective.EsclSearchMarginMs);
        Assert.Equal(50000, effective.DriverCooldownMs);
        Assert.Equal(310000, effective.DriverCooldownMaxMs);
        Assert.Equal(4500, effective.ScanQueueTimeoutMs);
        Assert.Equal(310000, effective.ScanOverallTimeoutMs);
        Assert.Equal(95000, effective.ScanNoProgressTimeoutMs);
        Assert.Equal(8500, effective.ShutdownTimeoutMs);
        Assert.Equal(30, effective.ScannersRequestTimeoutSeconds);
        Assert.Equal(410, effective.ScanRequestTimeoutSeconds);
        EsclManualDeviceConfiguration device = Assert.Single(effective.EsclManualDevices);
        Assert.Equal(SidecarTestData.OverrideDeviceName, device.Name);
        Assert.Equal(SidecarTestData.OverrideDeviceUrl, device.Address);
    }

    [Fact]
    public async Task LoadEffectiveConfiguration_WithSparseOverrides_MergesOverridesOverBase()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateSparseOverrides());
        Assert.True(writeResult.IsSuccess);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsSuccess);
        Assert.NotNull(loadResult.Value);
        ScannerServiceConfiguration effective = loadResult.Value;
        Assert.Equal(9000, effective.StatusCheckInterval);
        EsclManualDeviceConfiguration device = Assert.Single(effective.EsclManualDevices);
        Assert.Equal(SidecarTestData.OverrideDeviceUrl, device.Address);
        Assert.Equal(3000, effective.HttpTimeout);
        Assert.Equal(1500, effective.StartupDelay);
        Assert.Equal(20000, effective.DriverTimeoutMs);
        Assert.Equal(8000, effective.EsclSearchTimeoutMs);
        Assert.Equal(2500, effective.EsclSearchMarginMs);
        Assert.Equal(45000, effective.DriverCooldownMs);
        Assert.Equal(300000, effective.DriverCooldownMaxMs);
        Assert.Equal(4000, effective.ScanQueueTimeoutMs);
        Assert.Equal(300000, effective.ScanOverallTimeoutMs);
        Assert.Equal(90000, effective.ScanNoProgressTimeoutMs);
        Assert.Equal(8000, effective.ShutdownTimeoutMs);
        Assert.Equal(30, effective.ScannersRequestTimeoutSeconds);
        Assert.Equal(400, effective.ScanRequestTimeoutSeconds);
        Assert.Equal(7000, store.BaseConfig.StatusCheckInterval);
        Assert.Equal(SidecarTestData.BaseDeviceAddress, Assert.Single(store.BaseConfig.EsclManualDevices).Address);
    }

    [Fact]
    public async Task LoadEffectiveConfiguration_WithAllFieldsOverridden_KeepsBaseApiPortAndApiHost()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateFullOverrides());
        Assert.True(writeResult.IsSuccess);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsSuccess);
        Assert.NotNull(loadResult.Value);
        Assert.Equal(50123, loadResult.Value.ApiPort);
        Assert.Equal("127.0.0.1", loadResult.Value.ApiHost);
    }

    [Fact]
    public void LoadEffectiveConfiguration_WithCorruptSidecar_FailsAndQuarantinesContent()
    {
        const string corruptContent = "this is not json {{{";
        File.WriteAllText(SidecarBinState.SidecarPath, corruptContent);
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsFailure);
        Assert.StartsWith("appsettings.local.json:", loadResult.Error);
        Assert.Contains("not valid JSON", loadResult.Error);
        Assert.True(File.Exists(SidecarBinState.SidecarInvalidPath));
        Assert.Equal(corruptContent, File.ReadAllText(SidecarBinState.SidecarInvalidPath));
        Assert.False(File.Exists(SidecarBinState.SidecarPath));
    }

    [Fact]
    public void LoadEffectiveConfiguration_WithCorruptSidecar_SecondLoadReturnsBaseConfiguration()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "this is not json {{{");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        _ = store.LoadEffectiveConfiguration();

        Result<ScannerServiceConfiguration> secondLoadResult = store.LoadEffectiveConfiguration();

        Assert.True(secondLoadResult.IsSuccess);
        Assert.NotNull(secondLoadResult.Value);
        Assert.Equal(7000, secondLoadResult.Value.StatusCheckInterval);
        Assert.Equal(50123, secondLoadResult.Value.ApiPort);
        Assert.Equal("127.0.0.1", secondLoadResult.Value.ApiHost);
    }

    [Fact]
    public async Task LoadEffectiveConfiguration_WithInvalidMergedValues_FailsWithMergedSettingsError()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        ScannerSettingsOverridesDto overrides = new(
            null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null);
        Result writeResult = await store.WriteOverrides(overrides);
        Assert.True(writeResult.IsSuccess);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsFailure);
        Assert.StartsWith("merged settings:", loadResult.Error);
        Assert.Contains("HttpTimeout", loadResult.Error);
    }

    [Fact]
    public void LoadEffectiveConfiguration_WithInvalidBaseConfiguration_FailsWithBaseConfigurationError()
    {
        ScannerServiceConfiguration invalidBase = SidecarTestData.CreateBaseConfiguration();
        invalidBase.HttpTimeout = 0;
        var store = new LocalSettingsStore(invalidBase);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsFailure);
        Assert.StartsWith("appsettings.json:", loadResult.Error);
        Assert.Contains("HttpTimeout", loadResult.Error);
    }
}
