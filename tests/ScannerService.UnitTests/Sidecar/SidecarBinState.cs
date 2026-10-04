using System;
using System.IO;
using ScannerService.TrayApp.Configurations;

namespace ScannerService.UnitTests.Sidecar;

/// <summary>
/// Bin-folder hygiene for the settings-sidecar tests: LocalSettingsStore always targets
/// AppContext.BaseDirectory, which every class in this test assembly shares, so the sidecar
/// files (and scanner.db, owned by other bin-state test areas) are wiped before and after
/// every test.
/// </summary>
internal static class SidecarBinState
{
    public static string SidecarPath => Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName);

    public static string SidecarTempPath => Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName + ".tmp");

    public static string SidecarInvalidPath => Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName + ".invalid");

    public static void CleanSharedBinStateFiles()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DeleteIfPresent(SidecarTempPath);
        DeleteIfPresent(SidecarPath);
        DeleteIfPresent(SidecarInvalidPath);
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "scanner.db"));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
