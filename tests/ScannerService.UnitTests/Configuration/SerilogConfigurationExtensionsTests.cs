using System;
using System.IO;
using System.Reflection;
using ScannerService.TrayApp.Configurations;
using Serilog;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

/// <summary>
/// Covers the Serilog setup helpers that the passing (valid appsettings.json) start path never
/// varies: the rolling-interval parsing including its unknown-value fallback, and the
/// log-directory creation for a fresh base directory. The rolling-interval parser is private,
/// so it is invoked through reflection; no logger is created, so no log file is written.
/// </summary>
public class SerilogConfigurationExtensionsTests
{
    [Theory]
    [InlineData("minute", RollingInterval.Minute)]
    [InlineData("hour", RollingInterval.Hour)]
    [InlineData("DAY", RollingInterval.Day)]
    [InlineData("month", RollingInterval.Month)]
    [InlineData("year", RollingInterval.Year)]
    [InlineData("fortnight", RollingInterval.Day)]
    public void ParseRollingInterval_KnownAndUnknownValues_MapToExpectedInterval(string rawValue, RollingInterval expected)
    {
        MethodInfo parser = typeof(SerilogConfigurationExtensions).GetMethod(
            "ParseRollingInterval", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Private static ParseRollingInterval not found");

        object? parsed = parser.Invoke(null, new object[] { rawValue });

        Assert.Equal(expected, Assert.IsType<RollingInterval>(parsed));
    }

    [Fact]
    public void ConfigureFileLogging_WithMissingLogDirectory_CreatesTheDirectory()
    {
        string baseDirectory = Path.Combine(
            Path.GetTempPath(), "scanner-serilog-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loggingConfig = new LoggingConfiguration();
            string expectedLogDirectory = Path.Combine(baseDirectory, "logs");
            Assert.False(Directory.Exists(expectedLogDirectory));

            _ = new LoggerConfiguration().ConfigureFileLogging(loggingConfig, baseDirectory);

            Assert.True(Directory.Exists(expectedLogDirectory));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }
}
