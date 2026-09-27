using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace ScannerService.TrayApp;

/// <summary>
/// Ensures the Windows Firewall allows inbound mDNS (UDP 5353) for the app executable. eSCL network
/// scanner discovery (TWAIN/WIA-free scanning of network devices) sends multicast queries on UDP 5353
/// and the scanner's multicast answers are treated as unsolicited inbound traffic, which the default
/// firewall policy drops - the scanner then silently never appears in the device list. Adding the rule
/// requires elevation: when the app runs elevated the rule is created (and kept pointing at the
/// current executable) automatically; otherwise a single UAC prompt is offered, and if that is
/// declined the exact command an administrator can run is logged.
/// </summary>
internal static class NetworkDiscoveryFirewall
{
    private const string RuleName = "Resaa Scanner Service - mDNS discovery (UDP 5353)";
    private const string ExeName = "ScannerService.TrayApp.exe";
    private const int ErrorCanceled = 1223;

    public static void EnsureRule(bool isAdmin)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exePath))
        {
            Log.Debug("Firewall rule check skipped: executable not found at {ExePath}", exePath);
            return;
        }

        try
        {
            if (RuleExists())
            {
                if (isAdmin)
                {
                    // The rule may survive from an earlier install at a different path; recreate it so
                    // its program scope always points at the current executable.
                    DeleteRule();
                    AddRule(exePath);
                    Log.Debug("Refreshed firewall rule {RuleName} for {ExePath}", RuleName, exePath);
                }
                else
                {
                    Log.Debug("Firewall rule {RuleName} already exists", RuleName);
                }

                return;
            }

            if (isAdmin)
            {
                AddRule(exePath);
            }
            else
            {
                AddRuleElevated(exePath);
            }

            Log.Information("Added Windows Firewall rule {RuleName} for {ExePath} (required for eSCL network scanner discovery)",
                RuleName, exePath);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCanceled)
        {
            Log.Information(ex,
                "The firewall rule prompt was declined; eSCL network scanners may not be detected until the rule "
                + "is created: netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
                + "protocol=UDP localport=5353 profile=any program=\"{ExePath}\"",
                RuleName, exePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Failed to ensure the mDNS firewall rule; eSCL network scanners may not be detected. Run as "
                + "administrator: netsh advfirewall firewall add rule dir=in action=allow protocol=UDP "
                + "localport=5353 profile=any program=\"{ExePath}\" (name the rule \"{RuleName}\")",
                exePath, RuleName);
        }
    }

    private static bool RuleExists()
    {
        using var process = StartNetsh("advfirewall firewall show rule name=\"" + RuleName + "\"", elevate: false);
        // netsh exits with 1 when the named rule does not exist
        return WaitForExit(process) && process.ExitCode == 0;
    }

    private static void AddRule(string exePath)
    {
        using var process = StartNetsh(BuildAddRuleArguments(exePath), elevate: false);
        if (!WaitForExit(process) || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh did not add the mDNS firewall rule (exit code {process.ExitCode})");
        }
    }

    private static void DeleteRule()
    {
        using var process = StartNetsh("advfirewall firewall delete rule name=\"" + RuleName + "\"", elevate: false);
        WaitForExit(process);
    }

    /// <summary>
    /// Adds the rule through an elevated netsh (one UAC prompt). The default install is per-user and
    /// unelevated, so without this the rule would never be created; a decline (Win32Exception with
    /// ERROR_CANCELED) propagates to the caller and falls back to the logged manual command.
    /// </summary>
    private static void AddRuleElevated(string exePath)
    {
        using var process = StartNetsh(BuildAddRuleArguments(exePath), elevate: true);
        if (!WaitForExit(process) || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"elevated netsh did not add the mDNS firewall rule (exit code {process.ExitCode})");
        }
    }

    private static string BuildAddRuleArguments(string exePath)
    {
        return "advfirewall firewall add rule "
            + "name=\"" + RuleName + "\" "
            + "dir=in action=allow protocol=UDP localport=5353 profile=any "
            + "program=\"" + exePath + "\"";
    }

    private static bool WaitForExit(Process process)
    {
        if (!process.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        return true;
    }

    private static Process StartNetsh(string arguments, bool elevate)
    {
        // Output is deliberately not redirected: only the exit code matters, and an unread full
        // stdout pipe would make netsh block once its buffer fills.
        var netshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = netshPath,
            Arguments = arguments,
            UseShellExecute = elevate,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevate)
        {
            startInfo.Verb = "runas";
        }

        return Process.Start(startInfo)!;
    }
}
