namespace ScannerService.TrayApp.Configurations;

public class ScannerServiceConfiguration
{
    public int ApiPort { get; set; } = 58472;
    public int StatusCheckInterval { get; set; } = 5000;
    public int HttpTimeout { get; set; } = 2000;
    public int StartupDelay { get; set; } = 2000;

    // Device discovery budgets (see ScannerTimeouts in the Domain project for their meaning)
    public int DriverTimeoutMs { get; set; } = 15000;
    public int EsclSearchTimeoutMs { get; set; } = 4000;
    public int EsclSearchMarginMs { get; set; } = 2000;
    public int DriverCooldownMs { get; set; } = 60000;
    public int DriverCooldownMaxMs { get; set; } = 600000;

    // Scan job bounds
    public int ScanQueueTimeoutMs { get; set; } = 5000;
    public int ScanOverallTimeoutMs { get; set; } = 600000;
    public int ScanNoProgressTimeoutMs { get; set; } = 120000;

    // Host shutdown grace period for in-flight requests (and scanner worker teardown)
    public int ShutdownTimeoutMs { get; set; } = 5000;

    // HTTP request timeout policies; must leave headroom above the internal budgets so internal
    // timeout failures surface as structured 400 responses rather than a bare 408.
    public int ScannersRequestTimeoutSeconds { get; set; } = 25;
    public int ScanRequestTimeoutSeconds { get; set; } = 660;
}
