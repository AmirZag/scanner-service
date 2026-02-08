namespace ScannerService.TrayApp.Configurations;

public class ScannerServiceConfiguration
{
    public int ApiPort { get; set; } = 58472;
    public int StatusCheckInterval { get; set; } = 5000;
    public int HttpTimeout { get; set; } = 2000;
    public int StartupDelay { get; set; } = 2000;
}
