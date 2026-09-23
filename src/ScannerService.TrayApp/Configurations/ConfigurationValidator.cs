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

        // Validate device discovery budgets
        if (config.DriverTimeoutMs < 1000 || config.DriverTimeoutMs > 120000)
        {
            errors.Add($"DriverTimeoutMs must be between 1000ms and 120000ms, got {config.DriverTimeoutMs}");
        }

        if (config.EsclSearchTimeoutMs < 500 || config.EsclSearchTimeoutMs > config.DriverTimeoutMs)
        {
            errors.Add($"EsclSearchTimeoutMs must be between 500ms and DriverTimeoutMs ({config.DriverTimeoutMs}ms), got {config.EsclSearchTimeoutMs}");
        }

        if (config.EsclSearchMarginMs < 0 || config.EsclSearchMarginMs > 10000)
        {
            errors.Add($"EsclSearchMarginMs must be between 0ms and 10000ms, got {config.EsclSearchMarginMs}");
        }

        if (config.DriverCooldownMs < 5000 || config.DriverCooldownMs > 1800000)
        {
            errors.Add($"DriverCooldownMs must be between 5000ms and 1800000ms, got {config.DriverCooldownMs}");
        }

        if (config.DriverCooldownMaxMs < config.DriverCooldownMs || config.DriverCooldownMaxMs > 3600000)
        {
            errors.Add($"DriverCooldownMaxMs must be between DriverCooldownMs ({config.DriverCooldownMs}ms) and 3600000ms, got {config.DriverCooldownMaxMs}");
        }

        // Validate scan job bounds
        if (config.ScanQueueTimeoutMs < 0 || config.ScanQueueTimeoutMs > 60000)
        {
            errors.Add($"ScanQueueTimeoutMs must be between 0ms and 60000ms, got {config.ScanQueueTimeoutMs}");
        }

        if (config.ScanOverallTimeoutMs < 30000 || config.ScanOverallTimeoutMs > 3600000)
        {
            errors.Add($"ScanOverallTimeoutMs must be between 30000ms and 3600000ms, got {config.ScanOverallTimeoutMs}");
        }

        if (config.ScanNoProgressTimeoutMs < 10000 || config.ScanNoProgressTimeoutMs > config.ScanOverallTimeoutMs)
        {
            errors.Add($"ScanNoProgressTimeoutMs must be between 10000ms and ScanOverallTimeoutMs ({config.ScanOverallTimeoutMs}ms), got {config.ScanNoProgressTimeoutMs}");
        }

        // Validate shutdown grace period
        if (config.ShutdownTimeoutMs < 1000 || config.ShutdownTimeoutMs > 30000)
        {
            errors.Add($"ShutdownTimeoutMs must be between 1000ms and 30000ms, got {config.ShutdownTimeoutMs}");
        }

        // Validate HTTP request timeout policies (must leave headroom above the internal budgets)
        if (config.ScannersRequestTimeoutSeconds <= config.DriverTimeoutMs / 1000 + 5 || config.ScannersRequestTimeoutSeconds > 300)
        {
            errors.Add($"ScannersRequestTimeoutSeconds must be greater than DriverTimeoutMs + 5s ({config.DriverTimeoutMs / 1000 + 5}s) and at most 300s, got {config.ScannersRequestTimeoutSeconds}s");
        }

        if (config.ScanRequestTimeoutSeconds <= config.ScanOverallTimeoutMs / 1000 + 30 || config.ScanRequestTimeoutSeconds > 7200)
        {
            errors.Add($"ScanRequestTimeoutSeconds must be greater than ScanOverallTimeoutMs + 30s ({config.ScanOverallTimeoutMs / 1000 + 30}s) and at most 7200s, got {config.ScanRequestTimeoutSeconds}s");
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
