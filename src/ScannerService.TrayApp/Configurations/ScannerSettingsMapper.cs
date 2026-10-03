using System.Collections.Generic;
using System.Linq;
using ScannerService.Application.DTOs;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Pure mapping between the flat settings DTO (the settings API contract) and the full
/// ScannerServiceConfiguration. The API is flat GET/PUT like the other sections; the sparse
/// overrides model survives only INSIDE the sidecar file (unused fields of a hand-edited
/// file fall back to the base values). ApiPort and ApiHost have no counterpart in the DTO
/// (and unknown keys in a hand-edited sidecar are dropped by the JSON deserializer), so the
/// API bind can never be changed outside appsettings.json.
/// </summary>
public static class ScannerSettingsMapper
{
    /// <summary>Full configuration for validation: the PUT body's writable values plus the base
    /// ApiPort/ApiHost (which the API cannot change).</summary>
    public static ScannerServiceConfiguration ToConfiguration(ScannerServiceConfiguration baseConfig, ScannerSettingsDto dto)
    {
        return new ScannerServiceConfiguration
        {
            ApiPort = baseConfig.ApiPort,
            ApiHost = baseConfig.ApiHost,
            StatusCheckInterval = dto.StatusCheckInterval,
            HttpTimeout = dto.HttpTimeout,
            StartupDelay = dto.StartupDelay,
            DriverTimeoutMs = dto.DriverTimeoutMs,
            EsclSearchTimeoutMs = dto.EsclSearchTimeoutMs,
            EsclSearchMarginMs = dto.EsclSearchMarginMs,
            DriverCooldownMs = dto.DriverCooldownMs,
            DriverCooldownMaxMs = dto.DriverCooldownMaxMs,
            ScanQueueTimeoutMs = dto.ScanQueueTimeoutMs,
            ScanOverallTimeoutMs = dto.ScanOverallTimeoutMs,
            ScanNoProgressTimeoutMs = dto.ScanNoProgressTimeoutMs,
            ShutdownTimeoutMs = dto.ShutdownTimeoutMs,
            ScannersRequestTimeoutSeconds = dto.ScannersRequestTimeoutSeconds,
            ScanRequestTimeoutSeconds = dto.ScanRequestTimeoutSeconds,
            EsclManualDevices = ToDeviceConfigurations(dto.EsclManualDevices)
        };
    }

    /// <summary>The sidecar content for a full PUT: every writable field becomes an override.</summary>
    public static ScannerSettingsOverridesDto ToOverridesDto(ScannerSettingsDto dto)
    {
        return new ScannerSettingsOverridesDto(
            dto.StatusCheckInterval,
            dto.HttpTimeout,
            dto.StartupDelay,
            dto.DriverTimeoutMs,
            dto.EsclSearchTimeoutMs,
            dto.EsclSearchMarginMs,
            dto.DriverCooldownMs,
            dto.DriverCooldownMaxMs,
            dto.ScanQueueTimeoutMs,
            dto.ScanOverallTimeoutMs,
            dto.ScanNoProgressTimeoutMs,
            dto.ShutdownTimeoutMs,
            dto.ScannersRequestTimeoutSeconds,
            dto.ScanRequestTimeoutSeconds,
            dto.EsclManualDevices);
    }

    private static List<EsclManualDeviceSettingDto> ToDeviceSettingDtos(List<EsclManualDeviceConfiguration> devices)
    {
        return devices
            .Select(device => new EsclManualDeviceSettingDto { Name = device.Name, Address = device.Address })
            .ToList();
    }
    public static ScannerServiceConfiguration Merge(ScannerServiceConfiguration baseConfig, ScannerSettingsOverridesDto overrides)
    {
        ScannerServiceConfiguration merged = Clone(baseConfig);
        if (overrides.StatusCheckInterval.HasValue)
        {
            merged.StatusCheckInterval = overrides.StatusCheckInterval.Value;
        }
        if (overrides.HttpTimeout.HasValue)
        {
            merged.HttpTimeout = overrides.HttpTimeout.Value;
        }
        if (overrides.StartupDelay.HasValue)
        {
            merged.StartupDelay = overrides.StartupDelay.Value;
        }
        if (overrides.DriverTimeoutMs.HasValue)
        {
            merged.DriverTimeoutMs = overrides.DriverTimeoutMs.Value;
        }
        if (overrides.EsclSearchTimeoutMs.HasValue)
        {
            merged.EsclSearchTimeoutMs = overrides.EsclSearchTimeoutMs.Value;
        }
        if (overrides.EsclSearchMarginMs.HasValue)
        {
            merged.EsclSearchMarginMs = overrides.EsclSearchMarginMs.Value;
        }
        if (overrides.DriverCooldownMs.HasValue)
        {
            merged.DriverCooldownMs = overrides.DriverCooldownMs.Value;
        }
        if (overrides.DriverCooldownMaxMs.HasValue)
        {
            merged.DriverCooldownMaxMs = overrides.DriverCooldownMaxMs.Value;
        }
        if (overrides.ScanQueueTimeoutMs.HasValue)
        {
            merged.ScanQueueTimeoutMs = overrides.ScanQueueTimeoutMs.Value;
        }
        if (overrides.ScanOverallTimeoutMs.HasValue)
        {
            merged.ScanOverallTimeoutMs = overrides.ScanOverallTimeoutMs.Value;
        }
        if (overrides.ScanNoProgressTimeoutMs.HasValue)
        {
            merged.ScanNoProgressTimeoutMs = overrides.ScanNoProgressTimeoutMs.Value;
        }
        if (overrides.ShutdownTimeoutMs.HasValue)
        {
            merged.ShutdownTimeoutMs = overrides.ShutdownTimeoutMs.Value;
        }
        if (overrides.ScannersRequestTimeoutSeconds.HasValue)
        {
            merged.ScannersRequestTimeoutSeconds = overrides.ScannersRequestTimeoutSeconds.Value;
        }
        if (overrides.ScanRequestTimeoutSeconds.HasValue)
        {
            merged.ScanRequestTimeoutSeconds = overrides.ScanRequestTimeoutSeconds.Value;
        }
        if (overrides.EsclManualDevices != null)
        {
            merged.EsclManualDevices = ToDeviceConfigurations(overrides.EsclManualDevices);
        }
        return merged;
    }

    public static ScannerSettingsDto ToSettingsDto(ScannerServiceConfiguration config)
    {
        return new ScannerSettingsDto
        {
            StatusCheckInterval = config.StatusCheckInterval,
            HttpTimeout = config.HttpTimeout,
            StartupDelay = config.StartupDelay,
            DriverTimeoutMs = config.DriverTimeoutMs,
            EsclSearchTimeoutMs = config.EsclSearchTimeoutMs,
            EsclSearchMarginMs = config.EsclSearchMarginMs,
            DriverCooldownMs = config.DriverCooldownMs,
            DriverCooldownMaxMs = config.DriverCooldownMaxMs,
            ScanQueueTimeoutMs = config.ScanQueueTimeoutMs,
            ScanOverallTimeoutMs = config.ScanOverallTimeoutMs,
            ScanNoProgressTimeoutMs = config.ScanNoProgressTimeoutMs,
            ShutdownTimeoutMs = config.ShutdownTimeoutMs,
            ScannersRequestTimeoutSeconds = config.ScannersRequestTimeoutSeconds,
            ScanRequestTimeoutSeconds = config.ScanRequestTimeoutSeconds,
            EsclManualDevices = ToDeviceSettingDtos(config.EsclManualDevices)
        };
    }

    public static List<EsclManualDeviceConfiguration> ToDeviceConfigurations(List<EsclManualDeviceSettingDto> devices)
    {
        return devices
            .Select(device => new EsclManualDeviceConfiguration { Name = device.Name, Address = device.Address })
            .ToList();
    }

    /// <summary>Converts ConfigurationValidator output into a ValidationProblem dictionary keyed
    /// under the appsettings section name, consistent with the FluentValidation problem shape.</summary>
    public static Dictionary<string, string[]> ToValidationProblem((bool IsValid, List<string> Errors) validation)
    {
        return new Dictionary<string, string[]> { ["ScannerService"] = validation.Errors.ToArray() };
    }

    private static ScannerServiceConfiguration Clone(ScannerServiceConfiguration source)
    {
        return new ScannerServiceConfiguration
        {
            ApiPort = source.ApiPort,
            ApiHost = source.ApiHost,
            StatusCheckInterval = source.StatusCheckInterval,
            HttpTimeout = source.HttpTimeout,
            StartupDelay = source.StartupDelay,
            DriverTimeoutMs = source.DriverTimeoutMs,
            EsclSearchTimeoutMs = source.EsclSearchTimeoutMs,
            EsclSearchMarginMs = source.EsclSearchMarginMs,
            DriverCooldownMs = source.DriverCooldownMs,
            DriverCooldownMaxMs = source.DriverCooldownMaxMs,
            ScanQueueTimeoutMs = source.ScanQueueTimeoutMs,
            ScanOverallTimeoutMs = source.ScanOverallTimeoutMs,
            ScanNoProgressTimeoutMs = source.ScanNoProgressTimeoutMs,
            ShutdownTimeoutMs = source.ShutdownTimeoutMs,
            ScannersRequestTimeoutSeconds = source.ScannersRequestTimeoutSeconds,
            ScanRequestTimeoutSeconds = source.ScanRequestTimeoutSeconds,
            EsclManualDevices = source.EsclManualDevices
                .Select(device => new EsclManualDeviceConfiguration { Name = device.Name, Address = device.Address })
                .ToList()
        };
    }
}
