using System.Diagnostics;

namespace ScannerService.TrayApp;

/// <summary>
/// Shared netsh plumbing for the two firewall facades (ApiListenerFirewall, NetworkDiscoveryFirewall):
/// rule lookup, add/delete, and the bounded netsh process lifecycle. The facades keep their own rule
/// names, argument shapes, and log wording.
/// </summary>
internal static class NetshFirewallRule
{
    public static bool RuleExists(string ruleName)
    {
        using var process = StartNetsh("advfirewall firewall show rule name=\"" + ruleName + "\"", elevate: false);
        // netsh exits with 1 when the named rule does not exist
        return WaitForExit(process) && process.ExitCode == 0;
    }

    public static void AddRule(string ruleName, string arguments, bool elevate)
    {
        using var process = StartNetsh(arguments, elevate);
        if (!WaitForExit(process) || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh did not add the \"{ruleName}\" firewall rule (exit code {process.ExitCode})");
        }
    }

    public static void DeleteRule(string ruleName)
    {
        using var process = StartNetsh("advfirewall firewall delete rule name=\"" + ruleName + "\"", elevate: false);
        WaitForExit(process);
    }

    public static bool WaitForExit(Process process)
    {
        if (!process.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        return true;
    }

    public static Process StartNetsh(string arguments, bool elevate)
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
