using System.Collections.Generic;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

public class LoggingConfigurationValidationTests
{
    [Fact]
    public void Validate_NullConfiguration_ReportsMissingConfiguration()
    {
        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(null!);

        Assert.False(result.IsValid);
        Assert.Equal("Logging configuration is missing", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_NullFileSection_ReportsFileConfigurationMissing()
    {
        var configuration = new LoggingConfiguration { File = null! };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("Logging File configuration is missing", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_DefaultLoggingConfiguration_IsValidWithoutErrors()
    {
        var configuration = new LoggingConfiguration();

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespacePath_IsRejectedWithExactMessage(string path)
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.Path = path;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("Log path cannot be empty", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_UnknownRollingInterval_IsRejectedWithExactMessage()
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.RollingInterval = "Weekly";

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal("RollingInterval must be one of: Minute, Hour, Day, Month, Year", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("Minute")]
    [InlineData("Hour")]
    [InlineData("Day")]
    [InlineData("day")]
    [InlineData("MONTH")]
    [InlineData("Year")]
    public void Validate_RollingInterval_KnownValuesIgnoreCase_AreAccepted(string rollingInterval)
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.RollingInterval = rollingInterval;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void Validate_RetainedFileCountLimit_BoundaryValues_MatchRangeRules(int retainedFileCountLimit, bool expectedValid)
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.RetainedFileCountLimit = retainedFileCountLimit;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"RetainedFileCountLimit must be between 1 and 365, got {retainedFileCountLimit}", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(1048575L, false)]
    [InlineData(1048576L, true)]
    [InlineData(1073741824L, true)]
    [InlineData(1073741825L, false)]
    public void Validate_FileSizeLimitBytes_BoundaryValues_MatchRangeRules(long fileSizeLimitBytes, bool expectedValid)
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.FileSizeLimitBytes = fileSizeLimitBytes;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.Equal(expectedValid, result.IsValid);
        if (expectedValid)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.Equal($"FileSizeLimitBytes must be between 1MB and 1GB, got {fileSizeLimitBytes}", Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_MultipleInvalidFields_ReportsAllErrorsTogether()
    {
        LoggingConfiguration configuration = CreateValidLoggingConfiguration();
        configuration.File.Path = "";
        configuration.File.RollingInterval = "Weekly";
        configuration.File.RetainedFileCountLimit = 0;

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateLoggingConfiguration(configuration);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains("Log path cannot be empty", result.Errors);
        Assert.Contains("RollingInterval must be one of: Minute, Hour, Day, Month, Year", result.Errors);
        Assert.Contains("RetainedFileCountLimit must be between 1 and 365, got 0", result.Errors);
    }

    private static LoggingConfiguration CreateValidLoggingConfiguration()
    {
        return new LoggingConfiguration
        {
            File = new FileLoggingConfiguration
            {
                Path = "logs/scanner-.log",
                RollingInterval = "Day",
                RetainedFileCountLimit = 7,
                FileSizeLimitBytes = 10_485_760,
                RollOnFileSizeLimit = true
            }
        };
    }
}
