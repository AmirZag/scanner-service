using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using ScannerService.TrayApp;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

/// <summary>
/// These tests intentionally exercise the REAL netsh.exe read and failed-write paths of the two
/// firewall helper classes: RuleExists performs an unelevated rule query, and AddRule/DeleteRule
/// run without elevation, so add/delete attempts fail with an access-denied exit code. No UAC
/// prompt can ever appear (UseShellExecute stays false); the elevated paths (EnsureRule and
/// AddRuleElevated) are deliberately never touched. The helpers are private, so reflection is
/// used to reach them.
/// </summary>
public class FirewallHelpersTests
{
    private static object InvokePrivateStatic(Type type, string methodName, params object[] arguments)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Private static method " + type.Name + "." + methodName + " not found");
        return method.Invoke(null, arguments)!;
    }

    /// <summary>Invokes a private static helper expected to fail, unwrapping the reflection
    /// wrapper so the assertion sees the production exception type.</summary>
    private static Exception InvokePrivateStaticExpectingFailure(Type type, string methodName, params object[] arguments)
    {
        try
        {
            InvokePrivateStatic(type, methodName, arguments);
        }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException is not null)
        {
            return wrapper.InnerException;
        }

        throw new InvalidOperationException("Expected " + type.Name + "." + methodName + " to fail");
    }

    private static bool IsRunningElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [Fact]
    public void ApiListenerFirewall_RuleExists_QueriesNetshAndReturnsStableResult()
    {
        object firstResult = InvokePrivateStatic(typeof(ApiListenerFirewall), "RuleExists");
        object secondResult = InvokePrivateStatic(typeof(ApiListenerFirewall), "RuleExists");

        Assert.IsType<bool>(firstResult);
        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public void ApiListenerFirewall_AddRule_UnelevatedNetshFailsCleanly()
    {
        string missingExePath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            if (IsRunningElevated())
            {
                // An elevated run would genuinely create the rule; the DeleteRule below removes it again.
                InvokePrivateStatic(typeof(ApiListenerFirewall), "AddRule", missingExePath);
            }
            else
            {
                Exception failure = InvokePrivateStaticExpectingFailure(typeof(ApiListenerFirewall), "AddRule", missingExePath);
                Assert.IsType<InvalidOperationException>(failure);
                Assert.Contains("netsh did not add the", failure.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Exception? deleteFailure = Record.Exception(() => InvokePrivateStatic(typeof(ApiListenerFirewall), "DeleteRule"));
            Assert.Null(deleteFailure);
        }
    }

    [Fact]
    public void ApiListenerFirewall_BuildAddRuleArguments_ContainsRuleTokensAndExePath()
    {
        const string exePath = @"C:\Apps\ScannerService.TrayApp.exe";

        string arguments = Assert.IsType<string>(
            InvokePrivateStatic(typeof(ApiListenerFirewall), "BuildAddRuleArguments", exePath));

        Assert.Contains("advfirewall firewall add rule", arguments, StringComparison.Ordinal);
        Assert.Contains("name=\"Resaa Scanner Service - Web API (TCP)\"", arguments, StringComparison.Ordinal);
        Assert.Contains("dir=in action=allow protocol=TCP profile=any", arguments, StringComparison.Ordinal);
        Assert.Contains("program=\"C:\\Apps\\ScannerService.TrayApp.exe\"", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("localport", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkDiscoveryFirewall_RuleExists_QueriesNetshAndReturnsStableResult()
    {
        object firstResult = InvokePrivateStatic(typeof(NetworkDiscoveryFirewall), "RuleExists");
        object secondResult = InvokePrivateStatic(typeof(NetworkDiscoveryFirewall), "RuleExists");

        Assert.IsType<bool>(firstResult);
        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public void NetworkDiscoveryFirewall_AddRule_UnelevatedNetshFailsCleanly()
    {
        string missingExePath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            if (IsRunningElevated())
            {
                // An elevated run would genuinely create the rule; the DeleteRule below removes it again.
                InvokePrivateStatic(typeof(NetworkDiscoveryFirewall), "AddRule", missingExePath);
            }
            else
            {
                Exception failure = InvokePrivateStaticExpectingFailure(typeof(NetworkDiscoveryFirewall), "AddRule", missingExePath);
                Assert.IsType<InvalidOperationException>(failure);
                Assert.Contains("netsh did not add the", failure.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Exception? deleteFailure = Record.Exception(() => InvokePrivateStatic(typeof(NetworkDiscoveryFirewall), "DeleteRule"));
            Assert.Null(deleteFailure);
        }
    }

    [Fact]
    public void NetworkDiscoveryFirewall_BuildAddRuleArguments_ContainsRuleTokensAndExePath()
    {
        const string exePath = @"C:\Apps\ScannerService.TrayApp.exe";

        string arguments = Assert.IsType<string>(
            InvokePrivateStatic(typeof(NetworkDiscoveryFirewall), "BuildAddRuleArguments", exePath));

        Assert.Contains("advfirewall firewall add rule", arguments, StringComparison.Ordinal);
        Assert.Contains("name=\"Resaa Scanner Service - mDNS discovery (UDP 5353)\"", arguments, StringComparison.Ordinal);
        Assert.Contains("dir=in action=allow protocol=UDP localport=5353 profile=any", arguments, StringComparison.Ordinal);
        Assert.Contains("program=\"C:\\Apps\\ScannerService.TrayApp.exe\"", arguments, StringComparison.Ordinal);
    }
}
