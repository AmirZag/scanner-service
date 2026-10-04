using System;
using System.IO;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Sidecar;

[Collection("BinState")]
public class LocalSettingsStoreConstructionAndReadTests : IDisposable
{
    public LocalSettingsStoreConstructionAndReadTests()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public void Constructor_WithStaleTempFilePresent_DeletesTempFileAndKeepsSidecar()
    {
        File.WriteAllText(SidecarBinState.SidecarTempPath, "{}");
        File.WriteAllText(SidecarBinState.SidecarPath, "{}");

        _ = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Assert.False(File.Exists(SidecarBinState.SidecarTempPath));
        Assert.True(File.Exists(SidecarBinState.SidecarPath));
    }

    [Fact]
    public void SidecarExists_WithoutSidecarFile_ReturnsFalse()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Assert.False(store.SidecarExists);
    }

    [Fact]
    public void SidecarExists_WithSidecarFilePresent_ReturnsTrue()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "{}");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Assert.True(store.SidecarExists);
    }

    [Fact]
    public void ReadOverrides_WithoutSidecarFile_ReturnsAllNullOverrides()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();

        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(SidecarTestData.CreateEmptyOverrides(), readResult.Value);
    }

    [Fact]
    public void ReadOverrides_WithWhitespaceContent_ReturnsAllNullOverridesAndKeepsFile()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "   \n\t ");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();

        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(SidecarTestData.CreateEmptyOverrides(), readResult.Value);
        Assert.True(File.Exists(SidecarBinState.SidecarPath));
        Assert.False(File.Exists(SidecarBinState.SidecarInvalidPath));
    }

    [Fact]
    public void ReadOverrides_WithCamelCaseJson_DeserializesSparseValues()
    {
        const string rawJson = """
            {
              "statusCheckInterval": 9000,
              "driverTimeoutMs": 22000,
              "esclManualDevices": [
                {
                  "name": "HP Desk",
                  "address": "http://192.168.1.60:8080/eSCL"
                }
              ]
            }
            """;
        File.WriteAllText(SidecarBinState.SidecarPath, rawJson);
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();

        Assert.True(readResult.IsSuccess);
        Assert.NotNull(readResult.Value);
        Assert.Equal(9000, readResult.Value.StatusCheckInterval);
        Assert.Equal(22000, readResult.Value.DriverTimeoutMs);
        Assert.Null(readResult.Value.HttpTimeout);
        Assert.NotNull(readResult.Value.EsclManualDevices);
        EsclManualDeviceSettingDto device = Assert.Single(readResult.Value.EsclManualDevices);
        Assert.Equal(SidecarTestData.OverrideDeviceName, device.Name);
        Assert.Equal(SidecarTestData.OverrideDeviceUrl, device.Address);
    }

    [Fact]
    public void ReadSidecarSnapshot_WithoutSidecarFile_ReturnsNull()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        string? snapshot = store.ReadSidecarSnapshot();

        Assert.Null(snapshot);
    }

    [Fact]
    public void ReadSidecarSnapshot_WithSidecarFilePresent_ReturnsExactRawContent()
    {
        const string rawJson = "{ \"statusCheckInterval\": 9000 }";
        File.WriteAllText(SidecarBinState.SidecarPath, rawJson);
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());

        string? snapshot = store.ReadSidecarSnapshot();

        Assert.Equal(rawJson, snapshot);
    }
}
