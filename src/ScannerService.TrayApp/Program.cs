using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace ScannerService.TrayApp;

internal static class Program
{
    /// <summary>Machine-wide single-instance guard. Global scope: a second launch in any user
    /// session would otherwise silently land on port+1 with its own scanner stack and SQLite
    /// database, masked by the port-fallback search.</summary>
    private const string SingleInstanceMutexName = @"Global\ResaaScannerService";

    [STAThread]
    private static void Main()
    {
        // Single-instance guard: hold the named mutex for the process lifetime; a second launch
        // exits quietly - the first instance's tray icon is already on screen. An abandoned mutex
        // (a previous instance crashed while owning it) transfers ownership to this wait.
        using var singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        bool acquired;
        try
        {
            acquired = singleInstanceMutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            return;
        }

        // Check if running as admin
        bool isAdmin = IsRunAsAdministrator();

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp(isAdmin);
        System.Windows.Forms.Application.Run(trayApp);
    }

    internal static bool IsRunAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // Optional: Method to restart as admin if user chooses
    public static bool RestartAsAdministrator()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = System.Windows.Forms.Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(processInfo);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not restart as administrator: {ex.Message}",
                "Elevation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
    }
}
