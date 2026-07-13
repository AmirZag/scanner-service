using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace ScannerService.TrayApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Check if running as admin
        bool isAdmin = IsRunAsAdministrator();

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp(isAdmin);
        System.Windows.Forms.Application.Run(trayApp);
    }

    private static bool IsRunAsAdministrator()
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
