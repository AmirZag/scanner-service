namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Raw configuration for one manually configured eSCL network scanner, bound from the
/// "ScannerService:EsclManualDevices" section of appsettings.json.
/// </summary>
public class EsclManualDeviceConfiguration
{
    /// <summary>Display name; defaults to the host part of the address when omitted.</summary>
    public string? Name { get; set; }

    /// <summary>The scanner location: either a bare host name or IP (the HP default endpoint
    /// http://&lt;host&gt;:8080/eSCL is then assumed) or a full eSCL root URL such as
    /// "http://192.168.1.50:8080/eSCL".</summary>
    public string Address { get; set; } = string.Empty;
}
