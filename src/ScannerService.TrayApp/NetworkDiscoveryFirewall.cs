using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

    [ExcludeFromCodeCoverage(Justification = "May launch an elevated netsh process (UAC consent dialog); cannot execute in automated tests. The non-elevated netsh paths (RuleExists/DeleteRule/AddRule/StartNetsh) are tested directly.")]
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
        return NetshFirewallRule.RuleExists(RuleName);
    }

    private static void AddRule(string exePath)
    {
        NetshFirewallRule.AddRule(RuleName, BuildAddRuleArguments(exePath), elevate: false);
    }

    private static void DeleteRule()
    {
        NetshFirewallRule.DeleteRule(RuleName);
    }

    /// <summary>
    /// Adds the rule through an elevated netsh (one UAC prompt). The default install is per-user and
    /// unelevated, so without this the rule would never be created; a decline (Win32Exception with
    /// ERROR_CANCELED) propagates to the caller and falls back to the logged manual command.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Shells out with Verb=runas, triggering a UAC consent dialog; untestable in automation.")]
    private static void AddRuleElevated(string exePath)
    {
        NetshFirewallRule.AddRule(RuleName, BuildAddRuleArguments(exePath), elevate: true);
    }

    private static string BuildAddRuleArguments(string exePath)
    {
        return "advfirewall firewall add rule "
            + "name=\"" + RuleName + "\" "
            + "dir=in action=allow protocol=UDP localport=5353 profile=any "
            + "program=\"" + exePath + "\"";
    }
}
