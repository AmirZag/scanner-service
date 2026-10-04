using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

public class ScannerServiceConfigurationDefaultsTests
{
    [Fact]
    public void Constructor_SetsDocumentedDefaultValues()
    {
        var configuration = new ScannerServiceConfiguration();

        Assert.Equal(58472, configuration.ApiPort);
        Assert.Equal("localhost", configuration.ApiHost);
        Assert.Equal(5000, configuration.StatusCheckInterval);
        Assert.Equal(2000, configuration.HttpTimeout);
        Assert.Equal(2000, configuration.StartupDelay);
        Assert.Equal(15000, configuration.DriverTimeoutMs);
        Assert.Equal(8000, configuration.EsclSearchTimeoutMs);
        Assert.Equal(2000, configuration.EsclSearchMarginMs);
        Assert.Equal(60000, configuration.DriverCooldownMs);
        Assert.Equal(600000, configuration.DriverCooldownMaxMs);
        Assert.Equal(5000, configuration.ScanQueueTimeoutMs);
        Assert.Equal(600000, configuration.ScanOverallTimeoutMs);
        Assert.Equal(120000, configuration.ScanNoProgressTimeoutMs);
        Assert.Equal(5000, configuration.ShutdownTimeoutMs);
        Assert.Equal(25, configuration.ScannersRequestTimeoutSeconds);
        Assert.Equal(660, configuration.ScanRequestTimeoutSeconds);
        Assert.Empty(configuration.EsclManualDevices);
    }

    [Fact]
    public void Constructor_DefaultsEsclManualDevicesToNewEmptyListPerInstance()
    {
        var first = new ScannerServiceConfiguration();
        var second = new ScannerServiceConfiguration();

        Assert.NotSame(first.EsclManualDevices, second.EsclManualDevices);
        first.EsclManualDevices.Add(new EsclManualDeviceConfiguration { Address = "192.168.1.50" });
        Assert.Empty(second.EsclManualDevices);
    }

    [Fact]
    public void Validate_DefaultConfiguration_IsValidWithoutErrors()
    {
        var configuration = new ScannerServiceConfiguration();

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateScannerServiceConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AppSettingsJson_ScannerServiceSection_MatchesConfigurationDefaults()
    {
        string repositoryRootPath = FindRepositoryRootPath();
        string settingsPath = Path.Combine(repositoryRootPath, "src", "ScannerService.TrayApp", "appsettings.json");
        Assert.True(File.Exists(settingsPath), $"Expected the tray app settings file at '{settingsPath}'.");

        var configuration = new ScannerServiceConfiguration();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        JsonElement section = document.RootElement.GetProperty("ScannerService");

        Assert.Equal(ExpectedScannerServiceKeys(), ActualScannerServiceKeys(section));
        Assert.Equal(configuration.ApiPort, section.GetProperty("ApiPort").GetInt32());
        Assert.Equal(configuration.ApiHost, section.GetProperty("ApiHost").GetString());
        Assert.Equal(configuration.StatusCheckInterval, section.GetProperty("StatusCheckInterval").GetInt32());
        Assert.Equal(configuration.HttpTimeout, section.GetProperty("HttpTimeout").GetInt32());
        Assert.Equal(configuration.StartupDelay, section.GetProperty("StartupDelay").GetInt32());
        Assert.Equal(configuration.DriverTimeoutMs, section.GetProperty("DriverTimeoutMs").GetInt32());
        Assert.Equal(configuration.EsclSearchTimeoutMs, section.GetProperty("EsclSearchTimeoutMs").GetInt32());
        Assert.Equal(configuration.EsclSearchMarginMs, section.GetProperty("EsclSearchMarginMs").GetInt32());
        Assert.Equal(configuration.DriverCooldownMs, section.GetProperty("DriverCooldownMs").GetInt32());
        Assert.Equal(configuration.DriverCooldownMaxMs, section.GetProperty("DriverCooldownMaxMs").GetInt32());
        Assert.Equal(configuration.ScanQueueTimeoutMs, section.GetProperty("ScanQueueTimeoutMs").GetInt32());
        Assert.Equal(configuration.ScanOverallTimeoutMs, section.GetProperty("ScanOverallTimeoutMs").GetInt32());
        Assert.Equal(configuration.ScanNoProgressTimeoutMs, section.GetProperty("ScanNoProgressTimeoutMs").GetInt32());
        Assert.Equal(configuration.ShutdownTimeoutMs, section.GetProperty("ShutdownTimeoutMs").GetInt32());
        Assert.Equal(configuration.ScannersRequestTimeoutSeconds, section.GetProperty("ScannersRequestTimeoutSeconds").GetInt32());
        Assert.Equal(configuration.ScanRequestTimeoutSeconds, section.GetProperty("ScanRequestTimeoutSeconds").GetInt32());
        Assert.Empty(section.GetProperty("EsclManualDevices").EnumerateArray());
    }

    [Fact]
    public void AppSettingsJson_LoggingFileSection_MatchesLoggingConfigurationDefaults()
    {
        string repositoryRootPath = FindRepositoryRootPath();
        string settingsPath = Path.Combine(repositoryRootPath, "src", "ScannerService.TrayApp", "appsettings.json");
        Assert.True(File.Exists(settingsPath), $"Expected the tray app settings file at '{settingsPath}'.");

        var loggingConfiguration = new LoggingConfiguration();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        JsonElement fileSection = document.RootElement.GetProperty("Logging").GetProperty("File");

        Assert.Equal(loggingConfiguration.File.Path, fileSection.GetProperty("Path").GetString());
        Assert.Equal(loggingConfiguration.File.RollingInterval, fileSection.GetProperty("RollingInterval").GetString());
        Assert.Equal(loggingConfiguration.File.RetainedFileCountLimit, fileSection.GetProperty("RetainedFileCountLimit").GetInt32());
        Assert.Equal(loggingConfiguration.File.FileSizeLimitBytes, fileSection.GetProperty("FileSizeLimitBytes").GetInt64());
        Assert.Equal(loggingConfiguration.File.RollOnFileSizeLimit, fileSection.GetProperty("RollOnFileSizeLimit").GetBoolean());
    }

    private static string FindRepositoryRootPath()
    {
        string? candidatePath = AppContext.BaseDirectory;
        while (candidatePath is not null)
        {
            if (File.Exists(Path.Combine(candidatePath, "ScannerService.slnx")))
            {
                return candidatePath;
            }

            string? parentPath = Path.GetDirectoryName(candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parentPath == candidatePath)
            {
                break;
            }

            candidatePath = parentPath;
        }

        throw new InvalidOperationException("Could not locate the repository root by walking up from the test bin directory.");
    }

    private static string[] ActualScannerServiceKeys(JsonElement section)
    {
        return section.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static string[] ExpectedScannerServiceKeys()
    {
        return new[]
        {
            "ApiPort",
            "ApiHost",
            "StatusCheckInterval",
            "HttpTimeout",
            "StartupDelay",
            "DriverTimeoutMs",
            "EsclSearchTimeoutMs",
            "EsclSearchMarginMs",
            "DriverCooldownMs",
            "DriverCooldownMaxMs",
            "ScanQueueTimeoutMs",
            "ScanOverallTimeoutMs",
            "ScanNoProgressTimeoutMs",
            "ShutdownTimeoutMs",
            "ScannersRequestTimeoutSeconds",
            "ScanRequestTimeoutSeconds",
            "EsclManualDevices"
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
