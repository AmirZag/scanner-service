using System;
using System.IO;
using System.Threading.Tasks;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Sidecar;

/// <summary>
/// Failure-path coverage for LocalSettingsStore: locked sidecar/temp files, a missing or
/// section-less appsettings.json, quarantines that cannot complete, and the merge edge that
/// throws inside LoadEffectiveConfiguration. Joins the BinState collection because every path
/// targets the shared test bin directory - including appsettings.json itself, which
/// LoadBaseConfiguration reads from AppContext.BaseDirectory (renamed/rewritten only inside a
/// test and restored in a finally block).
/// </summary>
[Collection("BinState")]
public class LocalSettingsStoreFailurePathTests : IDisposable
{
    private static string BaseSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public LocalSettingsStoreFailurePathTests()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        SidecarBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public void Constructor_WithLockedStaleTempFile_SwallowsSweepFailureAndKeepsTempFile()
    {
        File.WriteAllText(SidecarBinState.SidecarTempPath, "{}");
        using FileStream lockStream = new FileStream(
            SidecarBinState.SidecarTempPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Exception? constructionFailure = Record.Exception(
            () => new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration()));

        lockStream.Dispose();
        Assert.Null(constructionFailure);
        Assert.True(File.Exists(SidecarBinState.SidecarTempPath));
    }

    [Fact]
    public void LoadBaseConfiguration_WhenAppsettingsJsonIsMissing_PropagatesLoadFailure()
    {
        WithMissingBaseSettings(() =>
        {
            Exception? loadFailure = Record.Exception(() => LocalSettingsStore.LoadBaseConfiguration());

            Assert.NotNull(loadFailure);
            Assert.Contains("appsettings.json", loadFailure!.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LoadBaseConfiguration_WithoutScannerServiceSection_FallsBackToDefaultInstance()
    {
        WithRewrittenBaseSettings("{ \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } } }", () =>
        {
            ScannerServiceConfiguration loaded = LocalSettingsStore.LoadBaseConfiguration();

            Assert.NotNull(loaded);
            Assert.Equal(58472, loaded.ApiPort);
            Assert.Equal("localhost", loaded.ApiHost);
            Assert.Equal(2000, loaded.HttpTimeout);
        });
    }

    [Fact]
    public void LoadBaseConfiguration_WithScannerServiceSection_BindsSectionValues()
    {
        const string json = """
            {
              "ScannerService": {
                "ApiPort": 51234,
                "ApiHost": "127.0.0.1",
                "HttpTimeout": 4444
              }
            }
            """;
        WithRewrittenBaseSettings(json, () =>
        {
            ScannerServiceConfiguration loaded = LocalSettingsStore.LoadBaseConfiguration();

            Assert.Equal(51234, loaded.ApiPort);
            Assert.Equal("127.0.0.1", loaded.ApiHost);
            Assert.Equal(4444, loaded.HttpTimeout);
        });
    }

    [Fact]
    public void LoadEffectiveConfiguration_WhenBaseDeviceListIsNull_FailsWithLoadErrorMessage()
    {
        // ScannerSettingsMapper.Clone dereferences EsclManualDevices unguarded, so a base
        // snapshot without a device list throws inside the load - the catch must turn that
        // into a named failure instead of letting the exception escape.
        ScannerServiceConfiguration baseWithoutDevices = SidecarTestData.CreateBaseConfiguration();
        baseWithoutDevices.EsclManualDevices = null!;
        var store = new LocalSettingsStore(baseWithoutDevices);

        Result<ScannerServiceConfiguration> loadResult = store.LoadEffectiveConfiguration();

        Assert.True(loadResult.IsFailure);
        Assert.StartsWith("Failed to load settings:", loadResult.Error);
    }

    [Fact]
    public void ReadOverrides_WhenSidecarFileIsLocked_FailsWithoutQuarantining()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "{}");
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        using FileStream lockStream = new FileStream(
            SidecarBinState.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Result<ScannerSettingsOverridesDto> readResult = store.ReadOverrides();

        lockStream.Dispose();
        Assert.True(readResult.IsFailure);
        Assert.Contains("could not be read", readResult.Error);
        Assert.True(File.Exists(SidecarBinState.SidecarPath));
        Assert.False(File.Exists(SidecarBinState.SidecarInvalidPath));
    }

    [Fact]
    public void QuarantineCorruptFile_WithoutSidecarFile_ReturnsWithoutCreatingFiles()
    {
        Exception? failure = Record.Exception(() => LocalSettingsStore.QuarantineCorruptFile());

        Assert.Null(failure);
        Assert.False(File.Exists(SidecarBinState.SidecarPath));
        Assert.False(File.Exists(SidecarBinState.SidecarInvalidPath));
    }

    [Fact]
    public void QuarantineCorruptFile_WhenSidecarFileIsLocked_KeepsCorruptFileOnDisk()
    {
        File.WriteAllText(SidecarBinState.SidecarPath, "{ not json");
        using FileStream lockStream = new FileStream(
            SidecarBinState.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Exception? failure = Record.Exception(() => LocalSettingsStore.QuarantineCorruptFile());

        lockStream.Dispose();
        Assert.Null(failure);
        Assert.True(File.Exists(SidecarBinState.SidecarPath));
        Assert.False(File.Exists(SidecarBinState.SidecarInvalidPath));
    }

    [Fact]
    public async Task WriteOverrides_WhenTempFileIsLocked_FailsAfterRetriesAndKeepsTempFile()
    {
        var store = new LocalSettingsStore(SidecarTestData.CreateBaseConfiguration());
        await File.WriteAllTextAsync(SidecarBinState.SidecarTempPath, "{}");
        using FileStream lockStream = new FileStream(
            SidecarBinState.SidecarTempPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Result writeResult = await store.WriteOverrides(SidecarTestData.CreateSparseOverrides());

        lockStream.Dispose();
        Assert.True(writeResult.IsFailure);
        Assert.StartsWith("Could not write appsettings.local.json after 3 attempts", writeResult.Error);
        Assert.True(File.Exists(SidecarBinState.SidecarTempPath));
        Assert.False(File.Exists(SidecarBinState.SidecarPath));
    }

    private static void WithMissingBaseSettings(Action testBody)
    {
        string backupPath = BaseSettingsPath + ".coverage-backup";
        File.Move(BaseSettingsPath, backupPath, true);
        try
        {
            testBody();
        }
        finally
        {
            File.Move(backupPath, BaseSettingsPath, true);
        }
    }

    private static void WithRewrittenBaseSettings(string json, Action testBody)
    {
        string backupPath = BaseSettingsPath + ".coverage-backup";
        File.Move(BaseSettingsPath, backupPath, true);
        try
        {
            File.WriteAllText(BaseSettingsPath, json);
            testBody();
        }
        finally
        {
            File.Move(backupPath, BaseSettingsPath, true);
        }
    }
}
