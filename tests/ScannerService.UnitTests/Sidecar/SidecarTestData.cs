using System.Collections.Generic;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;

namespace ScannerService.UnitTests.Sidecar;

/// <summary>
/// Hand-built fixtures for the settings-sidecar tests: a non-default but fully valid base
/// configuration (LoadEffectiveConfiguration validates both the base and the merged result,
/// and several fields are coupled across rules, e.g. EsclSearchTimeoutMs must stay within
/// DriverTimeoutMs) and override payloads whose values differ from every base value, so the
/// merge direction is observable in each assertion.
/// </summary>
internal static class SidecarTestData
{
    public const string BaseDeviceAddress = "10.0.0.5";
    public const string OverrideDeviceName = "HP Desk";
    public const string OverrideDeviceUrl = "http://192.168.1.60:8080/eSCL";

    public static ScannerServiceConfiguration CreateBaseConfiguration()
    {
        return new ScannerServiceConfiguration
        {
            ApiPort = 50123,
            ApiHost = "127.0.0.1",
            StatusCheckInterval = 7000,
            HttpTimeout = 3000,
            StartupDelay = 1500,
            DriverTimeoutMs = 20000,
            EsclSearchTimeoutMs = 8000,
            EsclSearchMarginMs = 2500,
            DriverCooldownMs = 45000,
            DriverCooldownMaxMs = 300000,
            ScanQueueTimeoutMs = 4000,
            ScanOverallTimeoutMs = 300000,
            ScanNoProgressTimeoutMs = 90000,
            EsclManualDevices = new List<EsclManualDeviceConfiguration>
            {
                new EsclManualDeviceConfiguration { Address = BaseDeviceAddress }
            },
            ShutdownTimeoutMs = 8000,
            ScannersRequestTimeoutSeconds = 30,
            ScanRequestTimeoutSeconds = 400
        };
    }

    public static ScannerSettingsOverridesDto CreateEmptyOverrides()
    {
        return new ScannerSettingsOverridesDto(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    public static ScannerSettingsOverridesDto CreateSparseOverrides()
    {
        return new ScannerSettingsOverridesDto(
            9000, null, null, null, null, null, null, null, null, null, null, null, null, null,
            new List<EsclManualDeviceSettingDto>
            {
                new EsclManualDeviceSettingDto { Name = OverrideDeviceName, Address = OverrideDeviceUrl }
            });
    }

    public static ScannerSettingsOverridesDto CreateFullOverrides()
    {
        return new ScannerSettingsOverridesDto(
            9000, 4000, 2500, 22000, 8500, 2600, 50000, 310000, 4500, 310000, 95000, 8500, 30, 410,
            new List<EsclManualDeviceSettingDto>
            {
                new EsclManualDeviceSettingDto { Name = OverrideDeviceName, Address = OverrideDeviceUrl }
            });
    }
}
