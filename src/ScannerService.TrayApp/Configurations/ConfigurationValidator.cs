using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScannerService.TrayApp.Configurations;

public static class ConfigurationValidator
{
    public static (bool IsValid, List<string> Errors) ValidateScannerServiceConfiguration(ScannerServiceConfiguration config)
    {
        var errors = new List<string>();

        if (config == null)
        {
            errors.Add("ScannerService configuration is missing");
            return (false, errors);
        }

        // Validate port
        if (config.ApiPort < 1024 || config.ApiPort > 65535)
        {
            errors.Add($"ApiPort must be between 1024 and 65535, got {config.ApiPort}");
        }

        // Validate StatusCheckInterval
        if (config.StatusCheckInterval < 1000 || config.StatusCheckInterval > 300000)
        {
            errors.Add($"StatusCheckInterval must be between 1000ms and 300000ms (5 minutes), got {config.StatusCheckInterval}");
        }

        // Validate HttpTimeout
        if (config.HttpTimeout < 100 || config.HttpTimeout > 60000)
        {
            errors.Add($"HttpTimeout must be between 100ms and 60000ms, got {config.HttpTimeout}");
        }

        // Validate StartupDelay
        if (config.StartupDelay < 0 || config.StartupDelay > 60000)
        {
            errors.Add($"StartupDelay must be between 0ms and 60000ms, got {config.StartupDelay}");
        }

        return (errors.Count == 0, errors);
    }

    public static (bool IsValid, List<string> Errors) ValidateLoggingConfiguration(LoggingConfiguration config)
    {
        var errors = new List<string>();

        if (config == null)
        {
            errors.Add("Logging configuration is missing");
            return (false, errors);
        }

        if (config.File == null)
        {
            errors.Add("Logging File configuration is missing");
            return (false, errors);
        }

        // Validate log path
        if (string.IsNullOrWhiteSpace(config.File.Path))
        {
            errors.Add("Log path cannot be empty");
        }

        // Validate rolling interval
        var validIntervals = new[] { "Minute", "Hour", "Day", "Month", "Year" };
        if (!validIntervals.Contains(config.File.RollingInterval, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"RollingInterval must be one of: {string.Join(", ", validIntervals)}");
        }

        // Validate retained file count
        if (config.File.RetainedFileCountLimit < 1 || config.File.RetainedFileCountLimit > 365)
        {
            errors.Add($"RetainedFileCountLimit must be between 1 and 365, got {config.File.RetainedFileCountLimit}");
        }

        // Validate file size limit
        if (config.File.FileSizeLimitBytes < 1048576 || config.File.FileSizeLimitBytes > 1073741824)
        {
            errors.Add($"FileSizeLimitBytes must be between 1MB and 1GB, got {config.File.FileSizeLimitBytes}");
        }

        return (errors.Count == 0, errors);
    }

    public static void ThrowIfInvalid(this (bool IsValid, List<string> Errors) validationResult, string configName)
    {
        if (!validationResult.IsValid)
        {
            var message = $"{configName} validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, validationResult.Errors)}";
            throw new InvalidOperationException(message);
        }
    }
}
