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
        if (!IsRunAsAdministrator())
        {
            RestartAsAdministrator();
            return;
        }

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp();
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

    private static void RestartAsAdministrator()
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
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"This application requires administrator privileges to run.\n\nError: {ex.Message}",
                "Administrator Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
