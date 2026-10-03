using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace ScannerService.TrayApp;

/// <summary>
/// Result of ensuring the Web API firewall rule; surfaced in the beyond-loopback security warning.
/// </summary>
internal enum ApiFirewallRuleOutcome
{
    /// <summary>The inbound rule was created.</summary>
    Created,
    /// <summary>An existing rule was recreated so its program scope points at the current executable.</summary>
    Refreshed,
    /// <summary>The rule already exists (unelevated run left it untouched).</summary>
    AlreadyExists,
    /// <summary>The UAC elevation prompt was declined; the manual netsh command was logged.</summary>
    PromptDeclined,
    /// <summary>Rule creation failed; the manual netsh command was logged.</summary>
    Failed,
    /// <summary>The executable was not found next to the entry assembly; nothing was attempted.</summary>
    ExeNotFound
}

/// <summary>
/// Ensures the Windows Firewall allows inbound TCP connections for the app executable, which is
/// required when the Web API binds beyond loopback (ScannerService:ApiHost set to a wildcard or an
/// IP address literal): remote clients' connections are otherwise silently dropped by the default
/// firewall policy. The rule is deliberately program-scoped without a fixed local port, because the
/// API port can change at runtime (port fallback) and only this executable listens on it. Adding
/// the rule requires elevation: when the app runs elevated the rule is created (and kept pointing
/// at the current executable) automatically; otherwise a single UAC prompt is offered, and if that
/// is declined the exact command an administrator can run is logged.
/// </summary>
internal static class ApiListenerFirewall
{
    private const string RuleName = "Resaa Scanner Service - Web API (TCP)";
    private const string ExeName = "ScannerService.TrayApp.exe";
    private const int ErrorCanceled = 1223;

    public static ApiFirewallRuleOutcome EnsureRule(bool isAdmin)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exePath))
        {
            Log.Debug("Firewall rule check skipped: executable not found at {ExePath}", exePath);
            return ApiFirewallRuleOutcome.ExeNotFound;
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
                    return ApiFirewallRuleOutcome.Refreshed;
                }

                Log.Debug("Firewall rule {RuleName} already exists", RuleName);
                return ApiFirewallRuleOutcome.AlreadyExists;
            }

            if (isAdmin)
            {
                AddRule(exePath);
            }
            else
            {
                AddRuleElevated(exePath);
            }

            Log.Information("Added Windows Firewall rule {RuleName} for {ExePath} (required because the Web API is configured to listen beyond loopback)",
                RuleName, exePath);
            return ApiFirewallRuleOutcome.Created;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCanceled)
        {
            Log.Information(ex,
                "The firewall rule prompt was declined; other machines cannot reach the Web API until the rule "
                + "is created: netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
                + "protocol=TCP profile=any program=\"{ExePath}\"",
                RuleName, exePath);
            return ApiFirewallRuleOutcome.PromptDeclined;
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Failed to ensure the Web API firewall rule; other machines cannot reach the Web API. Run as "
                + "administrator: netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
                + "protocol=TCP profile=any program=\"{ExePath}\"",
                RuleName, exePath);
            return ApiFirewallRuleOutcome.Failed;
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
            throw new InvalidOperationException($"netsh did not add the Web API firewall rule (exit code {process.ExitCode})");
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
            throw new InvalidOperationException($"elevated netsh did not add the Web API firewall rule (exit code {process.ExitCode})");
        }
    }

    private static string BuildAddRuleArguments(string exePath)
    {
        // No localport: the API port can change at runtime (port fallback) or via appsettings.json,
        // and a program-scoped rule grants nothing to other executables - only this process listens
        // on the API port.
        return "advfirewall firewall add rule "
            + "name=\"" + RuleName + "\" "
            + "dir=in action=allow protocol=TCP profile=any "
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
