using System.Collections.Generic;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

public class ScannerSettingsMapperTests
{
    [Fact]
    public void ToConfiguration_MapsEveryWritableFieldFromDto()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsDto dto = CreateDto();

        ScannerServiceConfiguration mapped = ScannerSettingsMapper.ToConfiguration(baseConfiguration, dto);

        Assert.Equal(dto.StatusCheckInterval, mapped.StatusCheckInterval);
        Assert.Equal(dto.HttpTimeout, mapped.HttpTimeout);
        Assert.Equal(dto.StartupDelay, mapped.StartupDelay);
        Assert.Equal(dto.DriverTimeoutMs, mapped.DriverTimeoutMs);
        Assert.Equal(dto.EsclSearchTimeoutMs, mapped.EsclSearchTimeoutMs);
        Assert.Equal(dto.EsclSearchMarginMs, mapped.EsclSearchMarginMs);
        Assert.Equal(dto.DriverCooldownMs, mapped.DriverCooldownMs);
        Assert.Equal(dto.DriverCooldownMaxMs, mapped.DriverCooldownMaxMs);
        Assert.Equal(dto.ScanQueueTimeoutMs, mapped.ScanQueueTimeoutMs);
        Assert.Equal(dto.ScanOverallTimeoutMs, mapped.ScanOverallTimeoutMs);
        Assert.Equal(dto.ScanNoProgressTimeoutMs, mapped.ScanNoProgressTimeoutMs);
        Assert.Equal(dto.ShutdownTimeoutMs, mapped.ShutdownTimeoutMs);
        Assert.Equal(dto.ScannersRequestTimeoutSeconds, mapped.ScannersRequestTimeoutSeconds);
        Assert.Equal(dto.ScanRequestTimeoutSeconds, mapped.ScanRequestTimeoutSeconds);
    }

    [Fact]
    public void ToConfiguration_AlwaysTakesApiPortAndApiHostFromBaseConfiguration()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsDto dto = CreateDto();

        ScannerServiceConfiguration mapped = ScannerSettingsMapper.ToConfiguration(baseConfiguration, dto);

        Assert.Equal(baseConfiguration.ApiPort, mapped.ApiPort);
        Assert.Equal(baseConfiguration.ApiHost, mapped.ApiHost);
    }

    [Fact]
    public void ToConfiguration_MapsManualDevicesToNewConfigurations()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsDto dto = CreateDto();

        ScannerServiceConfiguration mapped = ScannerSettingsMapper.ToConfiguration(baseConfiguration, dto);

        Assert.Equal(2, mapped.EsclManualDevices.Count);
        Assert.Null(mapped.EsclManualDevices[0].Name);
        Assert.Equal("192.168.1.60", mapped.EsclManualDevices[0].Address);
        Assert.Equal("Named Printer", mapped.EsclManualDevices[1].Name);
        Assert.Equal("http://192.168.1.61:8080/eSCL", mapped.EsclManualDevices[1].Address);
        Assert.NotSame(dto.EsclManualDevices[0], mapped.EsclManualDevices[0]);
        Assert.NotSame(dto.EsclManualDevices[1], mapped.EsclManualDevices[1]);
    }

    [Fact]
    public void ToOverridesDto_MapsEveryField()
    {
        ScannerSettingsDto dto = CreateDto();

        ScannerSettingsOverridesDto overridesDto = ScannerSettingsMapper.ToOverridesDto(dto);

        Assert.Equal(dto.StatusCheckInterval, overridesDto.StatusCheckInterval);
        Assert.Equal(dto.HttpTimeout, overridesDto.HttpTimeout);
        Assert.Equal(dto.StartupDelay, overridesDto.StartupDelay);
        Assert.Equal(dto.DriverTimeoutMs, overridesDto.DriverTimeoutMs);
        Assert.Equal(dto.EsclSearchTimeoutMs, overridesDto.EsclSearchTimeoutMs);
        Assert.Equal(dto.EsclSearchMarginMs, overridesDto.EsclSearchMarginMs);
        Assert.Equal(dto.DriverCooldownMs, overridesDto.DriverCooldownMs);
        Assert.Equal(dto.DriverCooldownMaxMs, overridesDto.DriverCooldownMaxMs);
        Assert.Equal(dto.ScanQueueTimeoutMs, overridesDto.ScanQueueTimeoutMs);
        Assert.Equal(dto.ScanOverallTimeoutMs, overridesDto.ScanOverallTimeoutMs);
        Assert.Equal(dto.ScanNoProgressTimeoutMs, overridesDto.ScanNoProgressTimeoutMs);
        Assert.Equal(dto.ShutdownTimeoutMs, overridesDto.ShutdownTimeoutMs);
        Assert.Equal(dto.ScannersRequestTimeoutSeconds, overridesDto.ScannersRequestTimeoutSeconds);
        Assert.Equal(dto.ScanRequestTimeoutSeconds, overridesDto.ScanRequestTimeoutSeconds);
        Assert.NotNull(overridesDto.EsclManualDevices);
        Assert.Equal(2, overridesDto.EsclManualDevices.Count);
        Assert.Null(overridesDto.EsclManualDevices[0].Name);
        Assert.Equal("192.168.1.60", overridesDto.EsclManualDevices[0].Address);
        Assert.Equal("Named Printer", overridesDto.EsclManualDevices[1].Name);
    }

    [Fact]
    public void Merge_AllOverridesNull_FallsBackToBaseValues()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsOverridesDto overrides = CreateEmptyOverrides();

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, overrides);

        Assert.Equal(baseConfiguration.StatusCheckInterval, merged.StatusCheckInterval);
        Assert.Equal(baseConfiguration.HttpTimeout, merged.HttpTimeout);
        Assert.Equal(baseConfiguration.StartupDelay, merged.StartupDelay);
        Assert.Equal(baseConfiguration.DriverTimeoutMs, merged.DriverTimeoutMs);
        Assert.Equal(baseConfiguration.EsclSearchTimeoutMs, merged.EsclSearchTimeoutMs);
        Assert.Equal(baseConfiguration.EsclSearchMarginMs, merged.EsclSearchMarginMs);
        Assert.Equal(baseConfiguration.DriverCooldownMs, merged.DriverCooldownMs);
        Assert.Equal(baseConfiguration.DriverCooldownMaxMs, merged.DriverCooldownMaxMs);
        Assert.Equal(baseConfiguration.ScanQueueTimeoutMs, merged.ScanQueueTimeoutMs);
        Assert.Equal(baseConfiguration.ScanOverallTimeoutMs, merged.ScanOverallTimeoutMs);
        Assert.Equal(baseConfiguration.ScanNoProgressTimeoutMs, merged.ScanNoProgressTimeoutMs);
        Assert.Equal(baseConfiguration.ShutdownTimeoutMs, merged.ShutdownTimeoutMs);
        Assert.Equal(baseConfiguration.ScannersRequestTimeoutSeconds, merged.ScannersRequestTimeoutSeconds);
        Assert.Equal(baseConfiguration.ScanRequestTimeoutSeconds, merged.ScanRequestTimeoutSeconds);
        Assert.Equal("Base Scanner", merged.EsclManualDevices[0].Name);
        Assert.Equal("http://10.0.0.9:8080/eSCL", merged.EsclManualDevices[0].Address);
    }

    [Fact]
    public void Merge_AllOverridesSet_OverridesWin()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsOverridesDto overrides = CreateFullOverrides();

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, overrides);

        Assert.Equal(1001, merged.StatusCheckInterval);
        Assert.Equal(1002, merged.HttpTimeout);
        Assert.Equal(1003, merged.StartupDelay);
        Assert.Equal(1004, merged.DriverTimeoutMs);
        Assert.Equal(1005, merged.EsclSearchTimeoutMs);
        Assert.Equal(1006, merged.EsclSearchMarginMs);
        Assert.Equal(1007, merged.DriverCooldownMs);
        Assert.Equal(1008, merged.DriverCooldownMaxMs);
        Assert.Equal(1009, merged.ScanQueueTimeoutMs);
        Assert.Equal(1010, merged.ScanOverallTimeoutMs);
        Assert.Equal(1011, merged.ScanNoProgressTimeoutMs);
        Assert.Equal(1012, merged.ShutdownTimeoutMs);
        Assert.Equal(1013, merged.ScannersRequestTimeoutSeconds);
        Assert.Equal(1014, merged.ScanRequestTimeoutSeconds);
        Assert.Equal("Override Scanner", merged.EsclManualDevices[0].Name);
        Assert.Equal("192.168.1.55", merged.EsclManualDevices[0].Address);
    }

    [Fact]
    public void Merge_FullOverrides_KeepsApiPortAndApiHostFromBaseConfiguration()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsOverridesDto overrides = CreateFullOverrides();

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, overrides);

        Assert.Equal(baseConfiguration.ApiPort, merged.ApiPort);
        Assert.Equal(baseConfiguration.ApiHost, merged.ApiHost);
    }

    [Fact]
    public void Merge_DoesNotMutateBaseConfiguration()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsOverridesDto overrides = CreateFullOverrides();

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, overrides);

        Assert.NotSame(baseConfiguration, merged);
        Assert.Equal(2001, baseConfiguration.StatusCheckInterval);
        Assert.Equal(2014, baseConfiguration.ScanRequestTimeoutSeconds);
        Assert.Equal("Base Scanner", baseConfiguration.EsclManualDevices[0].Name);
        Assert.Equal("http://10.0.0.9:8080/eSCL", baseConfiguration.EsclManualDevices[0].Address);
        Assert.Equal(1014, merged.ScanRequestTimeoutSeconds);
    }

    [Fact]
    public void Merge_ReturnsIndependentDeviceInstances()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsOverridesDto overrides = CreateEmptyOverrides();

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, overrides);

        Assert.NotSame(baseConfiguration.EsclManualDevices, merged.EsclManualDevices);
        Assert.NotSame(baseConfiguration.EsclManualDevices[0], merged.EsclManualDevices[0]);
        merged.EsclManualDevices[0].Address = "mutated";
        merged.EsclManualDevices.Add(new EsclManualDeviceConfiguration { Address = "extra" });
        Assert.Equal("http://10.0.0.9:8080/eSCL", baseConfiguration.EsclManualDevices[0].Address);
        Assert.Single(baseConfiguration.EsclManualDevices);
    }

    [Fact]
    public void ToSettingsDto_MapsEveryFieldFromConfiguration()
    {
        ScannerServiceConfiguration configuration = CreateBaseConfiguration();

        ScannerSettingsDto dto = ScannerSettingsMapper.ToSettingsDto(configuration);

        Assert.Equal(configuration.StatusCheckInterval, dto.StatusCheckInterval);
        Assert.Equal(configuration.HttpTimeout, dto.HttpTimeout);
        Assert.Equal(configuration.StartupDelay, dto.StartupDelay);
        Assert.Equal(configuration.DriverTimeoutMs, dto.DriverTimeoutMs);
        Assert.Equal(configuration.EsclSearchTimeoutMs, dto.EsclSearchTimeoutMs);
        Assert.Equal(configuration.EsclSearchMarginMs, dto.EsclSearchMarginMs);
        Assert.Equal(configuration.DriverCooldownMs, dto.DriverCooldownMs);
        Assert.Equal(configuration.DriverCooldownMaxMs, dto.DriverCooldownMaxMs);
        Assert.Equal(configuration.ScanQueueTimeoutMs, dto.ScanQueueTimeoutMs);
        Assert.Equal(configuration.ScanOverallTimeoutMs, dto.ScanOverallTimeoutMs);
        Assert.Equal(configuration.ScanNoProgressTimeoutMs, dto.ScanNoProgressTimeoutMs);
        Assert.Equal(configuration.ShutdownTimeoutMs, dto.ShutdownTimeoutMs);
        Assert.Equal(configuration.ScannersRequestTimeoutSeconds, dto.ScannersRequestTimeoutSeconds);
        Assert.Equal(configuration.ScanRequestTimeoutSeconds, dto.ScanRequestTimeoutSeconds);
        EsclManualDeviceSettingDto deviceDto = Assert.Single(dto.EsclManualDevices);
        Assert.Equal("Base Scanner", deviceDto.Name);
        Assert.Equal("http://10.0.0.9:8080/eSCL", deviceDto.Address);
    }

    [Fact]
    public void ToSettingsDto_EmptyDeviceList_MapsToEmptyList()
    {
        var configuration = new ScannerServiceConfiguration();

        ScannerSettingsDto dto = ScannerSettingsMapper.ToSettingsDto(configuration);

        Assert.Empty(dto.EsclManualDevices);
    }

    [Fact]
    public void ToDeviceConfigurations_MapsNameAndAddress()
    {
        var devices = new List<EsclManualDeviceSettingDto> { new() { Name = "Scanner One", Address = "192.168.1.50" } };

        List<EsclManualDeviceConfiguration> configurations = ScannerSettingsMapper.ToDeviceConfigurations(devices);

        EsclManualDeviceConfiguration configuration = Assert.Single(configurations);
        Assert.Equal("Scanner One", configuration.Name);
        Assert.Equal("192.168.1.50", configuration.Address);
    }

    [Fact]
    public void ToDeviceConfigurations_PreservesNullName()
    {
        var devices = new List<EsclManualDeviceSettingDto> { new() { Name = null, Address = "192.168.1.50" } };

        List<EsclManualDeviceConfiguration> configurations = ScannerSettingsMapper.ToDeviceConfigurations(devices);

        EsclManualDeviceConfiguration configuration = Assert.Single(configurations);
        Assert.Null(configuration.Name);
        Assert.Equal("192.168.1.50", configuration.Address);
    }

    [Fact]
    public void ToValidationProblem_InvalidResult_WrapsErrorsUnderScannerServiceKey()
    {
        (bool IsValid, List<string> Errors) validation = (false, new List<string> { "Alpha", "Beta" });

        Dictionary<string, string[]> problem = ScannerSettingsMapper.ToValidationProblem(validation);

        Assert.Single(problem);
        Assert.Equal(new[] { "Alpha", "Beta" }, problem["ScannerService"]);
    }

    [Fact]
    public void ToValidationProblem_ValidResult_WrapsEmptyErrorArray()
    {
        (bool IsValid, List<string> Errors) validation = (true, new List<string>());

        Dictionary<string, string[]> problem = ScannerSettingsMapper.ToValidationProblem(validation);

        Assert.Single(problem);
        Assert.Empty(problem["ScannerService"]);
    }

    [Fact]
    public void SettingsPutRoundTrip_ToOverridesDtoThenMerge_RestoresDtoValues()
    {
        ScannerServiceConfiguration baseConfiguration = CreateBaseConfiguration();
        ScannerSettingsDto dto = CreateDto();

        ScannerSettingsOverridesDto persisted = ScannerSettingsMapper.ToOverridesDto(dto);
        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(baseConfiguration, persisted);

        Assert.Equal(dto.StatusCheckInterval, merged.StatusCheckInterval);
        Assert.Equal(dto.HttpTimeout, merged.HttpTimeout);
        Assert.Equal(dto.StartupDelay, merged.StartupDelay);
        Assert.Equal(dto.DriverTimeoutMs, merged.DriverTimeoutMs);
        Assert.Equal(dto.EsclSearchTimeoutMs, merged.EsclSearchTimeoutMs);
        Assert.Equal(dto.EsclSearchMarginMs, merged.EsclSearchMarginMs);
        Assert.Equal(dto.DriverCooldownMs, merged.DriverCooldownMs);
        Assert.Equal(dto.DriverCooldownMaxMs, merged.DriverCooldownMaxMs);
        Assert.Equal(dto.ScanQueueTimeoutMs, merged.ScanQueueTimeoutMs);
        Assert.Equal(dto.ScanOverallTimeoutMs, merged.ScanOverallTimeoutMs);
        Assert.Equal(dto.ScanNoProgressTimeoutMs, merged.ScanNoProgressTimeoutMs);
        Assert.Equal(dto.ShutdownTimeoutMs, merged.ShutdownTimeoutMs);
        Assert.Equal(dto.ScannersRequestTimeoutSeconds, merged.ScannersRequestTimeoutSeconds);
        Assert.Equal(dto.ScanRequestTimeoutSeconds, merged.ScanRequestTimeoutSeconds);
        Assert.Equal(2, merged.EsclManualDevices.Count);
        Assert.Null(merged.EsclManualDevices[0].Name);
        Assert.Equal("192.168.1.60", merged.EsclManualDevices[0].Address);
        Assert.Equal("Named Printer", merged.EsclManualDevices[1].Name);
        Assert.Equal("http://192.168.1.61:8080/eSCL", merged.EsclManualDevices[1].Address);
        Assert.Equal(baseConfiguration.ApiPort, merged.ApiPort);
        Assert.Equal(baseConfiguration.ApiHost, merged.ApiHost);
    }

    private static ScannerServiceConfiguration CreateBaseConfiguration()
    {
        return new ScannerServiceConfiguration
        {
            ApiPort = 51000,
            ApiHost = "192.168.1.50",
            StatusCheckInterval = 2001,
            HttpTimeout = 2002,
            StartupDelay = 2003,
            DriverTimeoutMs = 2004,
            EsclSearchTimeoutMs = 2005,
            EsclSearchMarginMs = 2006,
            DriverCooldownMs = 2007,
            DriverCooldownMaxMs = 2008,
            ScanQueueTimeoutMs = 2009,
            ScanOverallTimeoutMs = 2010,
            ScanNoProgressTimeoutMs = 2011,
            ShutdownTimeoutMs = 2012,
            ScannersRequestTimeoutSeconds = 2013,
            ScanRequestTimeoutSeconds = 2014,
            EsclManualDevices = [new EsclManualDeviceConfiguration { Name = "Base Scanner", Address = "http://10.0.0.9:8080/eSCL" }]
        };
    }

    private static ScannerSettingsDto CreateDto()
    {
        return new ScannerSettingsDto
        {
            StatusCheckInterval = 1001,
            HttpTimeout = 1002,
            StartupDelay = 1003,
            DriverTimeoutMs = 1004,
            EsclSearchTimeoutMs = 1005,
            EsclSearchMarginMs = 1006,
            DriverCooldownMs = 1007,
            DriverCooldownMaxMs = 1008,
            ScanQueueTimeoutMs = 1009,
            ScanOverallTimeoutMs = 1010,
            ScanNoProgressTimeoutMs = 1011,
            ShutdownTimeoutMs = 1012,
            ScannersRequestTimeoutSeconds = 1013,
            ScanRequestTimeoutSeconds = 1014,
            EsclManualDevices =
            [
                new EsclManualDeviceSettingDto { Name = null, Address = "192.168.1.60" },
                new EsclManualDeviceSettingDto { Name = "Named Printer", Address = "http://192.168.1.61:8080/eSCL" }
            ]
        };
    }

    private static ScannerSettingsOverridesDto CreateEmptyOverrides()
    {
        return new ScannerSettingsOverridesDto(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    private static ScannerSettingsOverridesDto CreateFullOverrides()
    {
        return new ScannerSettingsOverridesDto(
            1001,
            1002,
            1003,
            1004,
            1005,
            1006,
            1007,
            1008,
            1009,
            1010,
            1011,
            1012,
            1013,
            1014,
            [new EsclManualDeviceSettingDto { Name = "Override Scanner", Address = "192.168.1.55" }]);
    }
}
