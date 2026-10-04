using System;
using System.Collections.Generic;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

public class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_NullConfiguration_ReportsMissingConfiguration()
    {
        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(null!);

        Assert.False(result.IsValid);
        Assert.Equal("ScannerService configuration is missing", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_DefaultConfiguration_IsValid()
    {
        var configuration = new ScannerServiceConfiguration();

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Validate_ApiPort_BoundaryValues_MatchRangeRules(int apiPort, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiPort = apiPort;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ApiPort must be between 1024 and 65535, got {apiPort}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("LocalHost")]
    [InlineData("loopback")]
    [InlineData("  localhost  ")]
    public void Validate_ApiHost_LoopbackTokens_AreAccepted(string apiHost)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiHost = apiHost;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ApiHost_NullHost_FallsBackToLoopbackAndIsAccepted()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiHost = null!;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("any")]
    [InlineData("ANY")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Validate_ApiHost_WildcardTokens_AreAccepted(string apiHost)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiHost = apiHost;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void Validate_ApiHost_CanonicalIpLiterals_AreAccepted(string apiHost)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiHost = apiHost;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("scanner.local")]
    [InlineData("192.168.1")]
    [InlineData("0")]
    [InlineData("my printer")]
    public void Validate_ApiHost_NonCanonicalHosts_AreRejectedWithExactMessage(string apiHost)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiHost = apiHost;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        string expectedMessage = $"ApiHost '{apiHost}' is not a valid bind address. Use \"localhost\", a wildcard (\"*\", \"+\", \"any\", \"0.0.0.0\", \"::\"), or a full IP address literal (e.g. \"192.168.1.50\"); DNS host names and abbreviated addresses are not supported";
        Assert.Equal(expectedMessage, Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(300000, true)]
    [InlineData(300001, false)]
    public void Validate_StatusCheckInterval_BoundaryValues_MatchRangeRules(int statusCheckInterval, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.StatusCheckInterval = statusCheckInterval;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"StatusCheckInterval must be between 1000ms and 300000ms (5 minutes), got {statusCheckInterval}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    public void Validate_HttpTimeout_BoundaryValues_MatchRangeRules(int httpTimeout, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.HttpTimeout = httpTimeout;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"HttpTimeout must be between 100ms and 60000ms, got {httpTimeout}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    public void Validate_StartupDelay_BoundaryValues_MatchRangeRules(int startupDelay, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.StartupDelay = startupDelay;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"StartupDelay must be between 0ms and 60000ms, got {startupDelay}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(120000, true)]
    [InlineData(120001, false)]
    public void Validate_DriverTimeoutMs_BoundaryValues_MatchRangeRules(int driverTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverTimeoutMs = driverTimeoutMs;
        // Keep the dependent budgets consistent so the only possible failure is the DriverTimeoutMs rule itself.
        configuration.EsclSearchTimeoutMs = Math.Min(configuration.EsclSearchTimeoutMs, driverTimeoutMs);
        configuration.ScannersRequestTimeoutSeconds = Math.Max(configuration.ScannersRequestTimeoutSeconds, (driverTimeoutMs / 1000) + 6);

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"DriverTimeoutMs must be between 1000ms and 120000ms, got {driverTimeoutMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(15000, true)]
    [InlineData(15001, false)]
    public void Validate_EsclSearchTimeoutMs_BoundaryValues_MatchRangeRules(int esclSearchTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.EsclSearchTimeoutMs = esclSearchTimeoutMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"EsclSearchTimeoutMs must be between 500ms and DriverTimeoutMs ({configuration.DriverTimeoutMs}ms), got {esclSearchTimeoutMs}", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_EsclSearchTimeoutMs_EqualToDriverTimeoutMs_IsAccepted()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverTimeoutMs = 5000;
        configuration.EsclSearchTimeoutMs = 5000;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EsclSearchTimeoutMs_AboveDriverTimeoutMs_IsRejectedWithExactMessage()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverTimeoutMs = 5000;
        configuration.EsclSearchTimeoutMs = 5001;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("EsclSearchTimeoutMs must be between 500ms and DriverTimeoutMs (5000ms), got 5001", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void Validate_EsclSearchMarginMs_BoundaryValues_MatchRangeRules(int esclSearchMarginMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.EsclSearchMarginMs = esclSearchMarginMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"EsclSearchMarginMs must be between 500ms and 10000ms, got {esclSearchMarginMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(4999, false)]
    [InlineData(5000, true)]
    [InlineData(1800000, true)]
    [InlineData(1800001, false)]
    public void Validate_DriverCooldownMs_BoundaryValues_MatchRangeRules(int driverCooldownMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverCooldownMs = driverCooldownMs;
        // Keep the upper cool-down bound consistent so the only possible failure is the DriverCooldownMs rule itself.
        configuration.DriverCooldownMaxMs = Math.Max(configuration.DriverCooldownMaxMs, driverCooldownMs);

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"DriverCooldownMs must be between 5000ms and 1800000ms, got {driverCooldownMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(59999, false)]
    [InlineData(60000, true)]
    [InlineData(3600000, true)]
    [InlineData(3600001, false)]
    public void Validate_DriverCooldownMaxMs_BoundaryValues_MatchRangeRules(int driverCooldownMaxMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverCooldownMaxMs = driverCooldownMaxMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"DriverCooldownMaxMs must be between DriverCooldownMs ({configuration.DriverCooldownMs}ms) and 3600000ms, got {driverCooldownMaxMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    public void Validate_ScanQueueTimeoutMs_BoundaryValues_MatchRangeRules(int scanQueueTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanQueueTimeoutMs = scanQueueTimeoutMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ScanQueueTimeoutMs must be between 0ms and 60000ms, got {scanQueueTimeoutMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(29999, false)]
    [InlineData(30000, true)]
    [InlineData(3600000, true)]
    [InlineData(3600001, false)]
    public void Validate_ScanOverallTimeoutMs_BoundaryValues_MatchRangeRules(int scanOverallTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanOverallTimeoutMs = scanOverallTimeoutMs;
        // Keep the dependent budgets consistent so the only possible failure is the ScanOverallTimeoutMs rule itself.
        configuration.ScanNoProgressTimeoutMs = Math.Min(configuration.ScanNoProgressTimeoutMs, scanOverallTimeoutMs);
        configuration.ScanRequestTimeoutSeconds = Math.Max(configuration.ScanRequestTimeoutSeconds, (scanOverallTimeoutMs / 1000) + 31);

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ScanOverallTimeoutMs must be between 30000ms and 3600000ms, got {scanOverallTimeoutMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(9999, false)]
    [InlineData(10000, true)]
    [InlineData(600000, true)]
    [InlineData(600001, false)]
    public void Validate_ScanNoProgressTimeoutMs_BoundaryValues_MatchRangeRules(int scanNoProgressTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanNoProgressTimeoutMs = scanNoProgressTimeoutMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ScanNoProgressTimeoutMs must be between 10000ms and ScanOverallTimeoutMs ({configuration.ScanOverallTimeoutMs}ms), got {scanNoProgressTimeoutMs}", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ScanNoProgressTimeoutMs_EqualToOverallTimeout_IsAccepted()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanOverallTimeoutMs = 50000;
        configuration.ScanNoProgressTimeoutMs = 50000;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ScanNoProgressTimeoutMs_AboveOverallTimeout_IsRejectedWithExactMessage()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanOverallTimeoutMs = 50000;
        configuration.ScanNoProgressTimeoutMs = 50001;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("ScanNoProgressTimeoutMs must be between 10000ms and ScanOverallTimeoutMs (50000ms), got 50001", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(30000, true)]
    [InlineData(30001, false)]
    public void Validate_ShutdownTimeoutMs_BoundaryValues_MatchRangeRules(int shutdownTimeoutMs, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ShutdownTimeoutMs = shutdownTimeoutMs;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ShutdownTimeoutMs must be between 1000ms and 30000ms, got {shutdownTimeoutMs}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Validate_ScannersRequestTimeoutSeconds_BoundaryValues_MatchRangeRules(int scannersRequestTimeoutSeconds, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScannersRequestTimeoutSeconds = scannersRequestTimeoutSeconds;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ScannersRequestTimeoutSeconds must be greater than DriverTimeoutMs + 5s ({configuration.DriverTimeoutMs / 1000 + 5}s) and at most 300s, got {scannersRequestTimeoutSeconds}s", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ScannersRequestTimeoutSeconds_AtIntegerDivisionThreshold_IsRejectedWithExactMessage()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverTimeoutMs = 10500;
        configuration.ScannersRequestTimeoutSeconds = 15;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("ScannersRequestTimeoutSeconds must be greater than DriverTimeoutMs + 5s (15s) and at most 300s, got 15s", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ScannersRequestTimeoutSeconds_AboveIntegerDivisionThreshold_IsAccepted()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.DriverTimeoutMs = 10500;
        configuration.ScannersRequestTimeoutSeconds = 16;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(630, false)]
    [InlineData(631, true)]
    [InlineData(7200, true)]
    [InlineData(7201, false)]
    public void Validate_ScanRequestTimeoutSeconds_BoundaryValues_MatchRangeRules(int scanRequestTimeoutSeconds, bool expectedValid)
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanRequestTimeoutSeconds = scanRequestTimeoutSeconds;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"ScanRequestTimeoutSeconds must be greater than ScanOverallTimeoutMs + 30s ({configuration.ScanOverallTimeoutMs / 1000 + 30}s) and at most 7200s, got {scanRequestTimeoutSeconds}s", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ScanRequestTimeoutSeconds_AtIntegerDivisionThreshold_IsRejectedWithExactMessage()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanOverallTimeoutMs = 30500;
        configuration.ScanNoProgressTimeoutMs = 30500;
        configuration.ScanRequestTimeoutSeconds = 60;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("ScanRequestTimeoutSeconds must be greater than ScanOverallTimeoutMs + 30s (60s) and at most 7200s, got 60s", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ScanRequestTimeoutSeconds_AboveIntegerDivisionThreshold_IsAccepted()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ScanOverallTimeoutMs = 30500;
        configuration.ScanNoProgressTimeoutMs = 30500;
        configuration.ScanRequestTimeoutSeconds = 61;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MultipleInvalidFields_ReportsAllErrorsTogether()
    {
        ScannerServiceConfiguration configuration = CreateValidConfiguration();
        configuration.ApiPort = 80;
        configuration.HttpTimeout = 50;
        configuration.ShutdownTimeoutMs = 200;
        configuration.EsclManualDevices.Add(new EsclManualDeviceConfiguration { Address = "fe80::1" });

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains("ApiPort must be between 1024 and 65535, got 80", result.Errors);
        Assert.Contains("HttpTimeout must be between 100ms and 60000ms, got 50", result.Errors);
        Assert.Contains("ShutdownTimeoutMs must be between 1000ms and 30000ms, got 200", result.Errors);
        Assert.Contains("EsclManualDevices[0].Address 'fe80::1' is a bare IPv6 address; use the full bracketed URL form instead, e.g. http://[fe80::1]:8080/eSCL", result.Errors);
    }

    private static ScannerServiceConfiguration CreateValidConfiguration()
    {
        return new ScannerServiceConfiguration();
    }
}
