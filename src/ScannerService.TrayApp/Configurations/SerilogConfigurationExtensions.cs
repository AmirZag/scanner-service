using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using ScannerService.TrayApp.Configurations;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Extension methods for configuring Serilog logging.
/// Eliminates duplicate logging setup code across the application.
/// </summary>
public static class SerilogConfigurationExtensions
{
    /// <summary>
    /// Configures Serilog file logging with the specified configuration.
    /// </summary>
    /// <param name="loggerConfiguration">The LoggerConfiguration to configure</param>
    /// <param name="loggingConfig">Logging configuration from appsettings.json</param>
    /// <param name="baseDirectory">Base directory for log file path (defaults to AppContext.BaseDirectory)</param>
    /// <returns>Configured LoggerConfiguration</returns>
    public static LoggerConfiguration ConfigureFileLogging(
        this LoggerConfiguration loggerConfiguration,
        LoggingConfiguration loggingConfig,
        string? baseDirectory = null)
    {
        var basePath = baseDirectory ?? AppContext.BaseDirectory;
        var logPath = System.IO.Path.Combine(basePath, loggingConfig.File.Path);
        var logDirectory = System.IO.Path.GetDirectoryName(logPath);

        // Ensure log directory exists
        if (!string.IsNullOrEmpty(logDirectory) && !System.IO.Directory.Exists(logDirectory))
        {
            System.IO.Directory.CreateDirectory(logDirectory);
        }

        var rollingInterval = ParseRollingInterval(loggingConfig.File.RollingInterval);

        return loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: loggingConfig.File.RetainedFileCountLimit,
                fileSizeLimitBytes: loggingConfig.File.FileSizeLimitBytes,
                rollOnFileSizeLimit: loggingConfig.File.RollOnFileSizeLimit,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    }

    /// <summary>
    /// Creates and configures the Serilog logger, setting it as the static Log instance.
    /// </summary>
    /// <param name="loggingConfig">Logging configuration from appsettings.json</param>
    /// <param name="baseDirectory">Base directory for log file path</param>
    public static void InitializeSerilog(LoggingConfiguration loggingConfig, string? baseDirectory = null)
    {
        Log.Logger = new LoggerConfiguration()
            .ConfigureFileLogging(loggingConfig, baseDirectory)
            .CreateLogger();
    }

    /// <summary>
    /// Loads the Logging section straight from appsettings.json, independent of any host builder.
    /// Used where logging must be initialized BEFORE the WebApplication builder exists (host
    /// restarts: the old host's Dispose flushes the static logger silent, and early startup steps
    /// like the firewall-rule logging must not land in that silent window).
    /// </summary>
    public static LoggingConfiguration LoadLoggingConfiguration()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        return configuration.GetSection("Logging").Get<LoggingConfiguration>() ?? new LoggingConfiguration();
    }

    private static RollingInterval ParseRollingInterval(string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "minute" => RollingInterval.Minute,
            "hour" => RollingInterval.Hour,
            "day" => RollingInterval.Day,
            "month" => RollingInterval.Month,
            "year" => RollingInterval.Year,
            _ => RollingInterval.Day
        };
    }
}
