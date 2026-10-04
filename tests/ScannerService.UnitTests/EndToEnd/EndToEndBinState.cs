using System;
using System.IO;

namespace ScannerService.UnitTests.EndToEnd;

/// <summary>
/// Bin-folder hygiene for the end-to-end tests: WebApiHostService creates scanner.db next to the
/// running process and LocalSettingsStore owns appsettings.local.json there, and both locations
/// resolve to the single shared test bin (AppContext.BaseDirectory). Files are wiped before and
/// after every test so tests never see another test's data.
/// </summary>
internal static class EndToEndBinState
{
    public static string SidecarPath => Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

    public static string SidecarInvalidPath => Path.Combine(AppContext.BaseDirectory, "appsettings.local.json.invalid");

    public static void CleanSharedBinStateFiles()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"));
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json.tmp"));
        DeleteIfPresent(SidecarInvalidPath);
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "scanner.db"));
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "scanner.db-journal"));
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "scanner.db-wal"));
        DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "scanner.db-shm"));
        DeletePackagedExecutableIfPresent();
    }

    private static void DeletePackagedExecutableIfPresent()
    {
        // NetworkDiscoveryFirewall.EnsureRule (called on every host start) only acts when the
        // packaged executable exists next to the running process; the build copies it into the
        // test bin because the test project references the WinExe. Removing it sends the firewall
        // helper down its documented "executable not found" skip path, so host starts in tests
        // never spawn netsh - and on a machine without the mDNS rule never trigger a UAC prompt.
        try
        {
            DeleteIfPresent(Path.Combine(AppContext.BaseDirectory, "ScannerService.TrayApp.exe"));
        }
        catch (UnauthorizedAccessException)
        {
            // A transient lock must not fail the test suite; without the delete the firewall step
            // merely performs a read-only netsh query instead of skipping outright.
        }
        catch (IOException)
        {
            // Same rationale as above: the skip path is an optimization, not a correctness need.
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
