using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Sidecar;

[Collection("BinState")]
public class LocalSettingsStoreWriteDeleteTests : IDisposable
{
    public LocalSettingsStoreWriteDeleteTests()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task WriteOverrides_SparseDto_PersistsNonNullValuesWithExplicitNullsForSparseFields()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateSparseOverrides());

        Assert.True(writeResult.IsSuccess);
        string rawJson = File.ReadAllText(SidecarBinState.SidecarPath);
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("statusCheckInterval", out JsonElement statusCheckInterval));
        Assert.Equal(9000, statusCheckInterval.GetInt32());
        Assert.True(root.TryGetProperty("httpTimeout", out JsonElement httpTimeout));
        Assert.Equal(JsonValueKind.Null, httpTimeout.ValueKind);
        Assert.True(root.TryGetProperty("esclManualDevices", out JsonElement devices));
        Assert.Equal(JsonValueKind.Array, devices.ValueKind);
        JsonElement device = devices[0];
        Assert.Equal(SidecarTestData.OverrideDeviceName, device.GetProperty("name").GetString());
        Assert.Equal(SidecarTestData.OverrideDeviceUrl, device.GetProperty("address").GetString());
    }

    [Fact]
    public async Task WriteOverrides_SecondWriteReplacesFirstValue()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        ScannerSettingsOverridesDto firstOverrides = SidecarTestData.CreateSparseOverrides();
        ScannerSettingsOverridesDto secondOverrides = new(
            11000, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        Result firstWriteResult = await store.WriteOverrides(firstOverrides);
        Result secondWriteResult = await store.WriteOverrides(secondOverrides);

        Assert.True(firstWriteResult.IsSuccess);
        Assert.True(secondWriteResult.IsSuccess);
        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();
        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(11000, readResult.Value.StatusCheckInterval);
        Assert.Null(readResult.Value.EsclManualDevices);
        string? snapshot = store.ReadSidecarSnapshot();
        Assert.NotNull(snapshot);
        Assert.DoesNotContain(SidecarTestData.OverrideDeviceUrl, snapshot);
    }

    [Fact]
    public async Task WriteOverrides_WithAllFifteenFieldsSet_RoundTripsEveryValue()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateFullOverrides());

        Assert.True(writeResult.IsSuccess);
        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();
        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(9000, readResult.Value.StatusCheckInterval);
        Assert.Equal(4000, readResult.Value.HttpTimeout);
        Assert.Equal(2500, readResult.Value.StartupDelay);
        Assert.Equal(22000, readResult.Value.DriverTimeoutMs);
        Assert.Equal(8500, readResult.Value.EsclSearchTimeoutMs);
        Assert.Equal(2600, readResult.Value.EsclSearchMarginMs);
        Assert.Equal(50000, readResult.Value.DriverCooldownMs);
        Assert.Equal(310000, readResult.Value.DriverCooldownMaxMs);
        Assert.Equal(4500, readResult.Value.ScanQueueTimeoutMs);
        Assert.Equal(310000, readResult.Value.ScanOverallTimeoutMs);
        Assert.Equal(95000, readResult.Value.ScanNoProgressTimeoutMs);
        Assert.Equal(8500, readResult.Value.ShutdownTimeoutMs);
        Assert.Equal(30, readResult.Value.ScannersRequestTimeoutSeconds);
        Assert.Equal(410, readResult.Value.ScanRequestTimeoutSeconds);
        Assert.NotNull(readResult.Value.EsclManualDevices);
        EsclManualDeviceSettingDto device = Assert.Single(readResult.Value.EsclManualDevices);
        Assert.Equal(SidecarTestData.OverrideDeviceName, device.Name);
        Assert.Equal(SidecarTestData.OverrideDeviceUrl, device.Address);
    }

    [Fact]
    public async Task WriteOverrides_OverCorruptContent_ReplacesFileWithValidJson()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "definitely not json {{{");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateSparseOverrides());

        Assert.True(writeResult.IsSuccess);
        Assert.False(File.Exists(SidecarBinState.SidecarInvalidPath));
        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();
        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(9000, readResult.Value.StatusCheckInterval);
    }

    [Fact]
    public async Task WriteOverrides_WhenSidecarFileLocked_FailsAfterRetriesKeepingOriginalContent()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        File.WriteAllText(SidecarBinState.SidecarPath, "{}");
        using FileStream lockStream = new FileStream(
            SidecarBinState.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateSparseOverrides());

        lockStream.Dispose();
        Assert.True(writeResult.IsFailure);
        Assert.StartsWith("Could not write appsettings.local.json after 3 attempts", writeResult.Error);
        Assert.Equal("{}", File.ReadAllText(SidecarBinState.SidecarPath));
        Assert.False(File.Exists(SidecarBinState.SidecarTempPath));
    }

    [Fact]
    public void DeleteOverrides_WithSidecarFilePresent_RemovesFile()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "{}");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result deleteResult = store.DeleteOverrides();

        Assert.True(deleteResult.IsSuccess);
        Assert.False(store.SidecarExists);
        Assert.False(File.Exists(SidecarBinState.SidecarPath));
    }

    [Fact]
    public void DeleteOverrides_WithoutSidecarFile_ReturnsSuccessWithoutCreatingFile()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result deleteResult = store.DeleteOverrides();

        Assert.True(deleteResult.IsSuccess);
        Assert.False(File.Exists(SidecarBinState.SidecarPath));
    }
}
