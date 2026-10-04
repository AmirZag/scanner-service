using System.Collections.Generic;
using ScannerService.Domain.Common;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

public class EsclManualDeviceValidationTests
{
    [Fact]
    public void ValidateDevices_NullList_IsValidWithoutErrors()
    {
        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(null);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateDevices_EmptyList_IsValidWithoutErrors()
    {
        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices([]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateDevices_EmptyOrWhitespaceAddress_IsRejectedWithExactMessage(string address)
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = address } };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.False(result.IsValid);
        // A single error pins the continue: no additional host-name error is produced for the same entry.
        Assert.Equal("EsclManualDevices[0].Address cannot be empty", Assert.Single(result.Errors));
    }

    [Fact]
    public void ValidateDevices_NullAddress_IsRejectedWithExactMessage()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = null! } };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.False(result.IsValid);
        Assert.Equal("EsclManualDevices[0].Address cannot be empty", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("printer.office.local")]
    [InlineData("http://192.168.1.50:8080/eSCL")]
    [InlineData("https://printer.local:8443/eSCL")]
    public void ValidateDevices_ValidAddresses_AreAcceptedWithoutErrors(string address)
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = address } };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateDevices_BareIpv6Address_IsRejectedWithBracketedUrlGuidance()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = "fe80::1" } };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.False(result.IsValid);
        Assert.Equal("EsclManualDevices[0].Address 'fe80::1' is a bare IPv6 address; use the full bracketed URL form instead, e.g. http://[fe80::1]:8080/eSCL", Assert.Single(result.Errors));
    }

    [Fact]
    public void ValidateDevices_InvalidHostName_IsRejectedWithExactMessage()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = "in valid" } };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.False(result.IsValid);
        Assert.Equal("EsclManualDevices[0].Address 'in valid' is neither a valid host name/IP nor an absolute http(s) URL", Assert.Single(result.Errors));
    }

    [Fact]
    public void ValidateDevices_MultipleInvalidEntries_ReportIndexOfEachOffendingEntry()
    {
        var devices = new List<EsclManualDeviceConfiguration>
        {
            new() { Address = "192.168.1.50" },
            new() { Address = string.Empty },
            new() { Address = "fe80::1" }
        };

        (bool IsValid, List<string> Errors) result = ConfigurationValidator.ValidateEsclManualDevices(devices);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("EsclManualDevices[1].Address cannot be empty", result.Errors);
        Assert.Contains("EsclManualDevices[2].Address 'fe80::1' is a bare IPv6 address; use the full bracketed URL form instead, e.g. http://[fe80::1]:8080/eSCL", result.Errors);
    }

    [Fact]
    public void Normalize_NullList_ReturnsEmptyList()
    {
        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(null);

        Assert.Empty(normalized);
    }

    [Fact]
    public void Normalize_EmptyList_ReturnsEmptyList()
    {
        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices([]);

        Assert.Empty(normalized);
    }

    [Fact]
    public void Normalize_BareIpv4Address_DefaultsNameToHostAndBuildsDefaultEsclUrl()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = "192.168.1.50" } };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        EsclManualDevice device = Assert.Single(normalized);
        Assert.Equal("192.168.1.50", device.Name);
        Assert.Equal("http://192.168.1.50:8080/eSCL", device.Address);
    }

    [Fact]
    public void Normalize_BareHostName_DefaultsNameToHostAndBuildsDefaultEsclUrl()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = "printer.office.local" } };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        EsclManualDevice device = Assert.Single(normalized);
        Assert.Equal("printer.office.local", device.Name);
        Assert.Equal("http://printer.office.local:8080/eSCL", device.Address);
    }

    [Fact]
    public void Normalize_HttpUrlAddress_IsAcceptedByDesignAndTrimsTrailingSlash()
    {
        var devices = new List<EsclManualDeviceConfiguration>
        {
            new() { Name = "  HP M428  ", Address = "http://192.168.1.50:8080/eSCL/" }
        };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        EsclManualDevice device = Assert.Single(normalized);
        Assert.Equal("HP M428", device.Name);
        Assert.Equal("http://192.168.1.50:8080/eSCL", device.Address);
    }

    [Fact]
    public void Normalize_HttpsUrlAddress_IsAcceptedByDesign()
    {
        var devices = new List<EsclManualDeviceConfiguration>
        {
            new() { Name = "Secure Printer", Address = "https://printer.local:8443/eSCL" }
        };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        EsclManualDevice device = Assert.Single(normalized);
        Assert.Equal("Secure Printer", device.Name);
        Assert.Equal("https://printer.local:8443/eSCL", device.Address);
    }

    [Fact]
    public void Normalize_BracketedIpv6Url_DefaultsNameToBracketedHost()
    {
        var devices = new List<EsclManualDeviceConfiguration> { new() { Address = "http://[fe80::1]:8080/eSCL" } };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        EsclManualDevice device = Assert.Single(normalized);
        Assert.Equal("[fe80::1]", device.Name);
        Assert.Equal("http://[fe80::1]:8080/eSCL", device.Address);
    }

    [Fact]
    public void Normalize_DuplicateAddresses_ArePassedThroughWithoutDeduplication()
    {
        var devices = new List<EsclManualDeviceConfiguration>
        {
            new() { Address = "192.168.1.50" },
            new() { Address = "192.168.1.50" }
        };

        List<EsclManualDevice> normalized = ConfigurationValidator.NormalizeEsclManualDevices(devices);

        Assert.Equal(2, normalized.Count);
        Assert.Equal(normalized[0], normalized[1]);
    }
}
