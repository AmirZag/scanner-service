namespace ScannerService.Domain.Common;

/// <summary>
/// Immutable timeout settings for scanner operations.
/// Bound once at startup from configuration; safe to share across threads.
/// </summary>
public sealed class ScannerTimeouts
{
    /// <summary>Maximum time to wait for a single driver (WIA/TWAIN) to enumerate its devices.</summary>
    public int DriverTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultDriverTimeoutMs;

    /// <summary>Maximum time for the ESCL (network) mDNS device search.</summary>
    public int EsclSearchTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultEsclSearchTimeoutMs;

    /// <summary>Extra margin added on top of the ESCL search timeout; the ESCL driver budget must exceed the
    /// internal search timeout or every ESCL query would be (incorrectly) treated as a timeout.</summary>
    public int EsclSearchMarginMs { get; init; } = ScannerConstants.Timeouts.EsclSearchMarginMs;

    /// <summary>Cool-down applied to a driver after its first timeout; grows exponentially on repeated timeouts.</summary>
    public int DriverCooldownMs { get; init; } = ScannerConstants.Timeouts.DefaultDriverCooldownMs;

    /// <summary>Upper bound for the exponential driver cool-down.</summary>
    public int DriverCooldownMaxMs { get; init; } = ScannerConstants.Timeouts.DefaultDriverCooldownMaxMs;

    /// <summary>How long a scan request waits for a busy scanner before failing fast.</summary>
    public int ScanQueueTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultScanQueueTimeoutMs;

    /// <summary>Overall cap for a single scan job.</summary>
    public int ScanOverallTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultScanOverallTimeoutMs;

    /// <summary>Watchdog re-armed after every scanned page; a scan with no progress for this long is failed.</summary>
    public int ScanNoProgressTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultScanNoProgressTimeoutMs;

    /// <summary>Grace period for the web host (and therefore in-flight requests) to stop during shutdown.</summary>
    public int ShutdownTimeoutMs { get; init; } = ScannerConstants.Timeouts.DefaultShutdownTimeoutMs;

    /// <summary>Budget for WIA/TWAIN device enumeration.</summary>
    public TimeSpan DriverTimeout => TimeSpan.FromMilliseconds(DriverTimeoutMs);

    /// <summary>Budget for the ESCL driver: internal search timeout plus margin.</summary>
    public TimeSpan EsclDeviceSearchBudget => TimeSpan.FromMilliseconds(EsclSearchTimeoutMs + EsclSearchMarginMs);

    /// <summary>Wait for a busy scanner before failing a scan request.</summary>
    public TimeSpan ScanQueueTimeout => TimeSpan.FromMilliseconds(ScanQueueTimeoutMs);

    /// <summary>Overall scan job cap.</summary>
    public TimeSpan ScanOverallTimeout => TimeSpan.FromMilliseconds(ScanOverallTimeoutMs);

    /// <summary>No-progress watchdog for scan jobs.</summary>
    public TimeSpan ScanNoProgressTimeout => TimeSpan.FromMilliseconds(ScanNoProgressTimeoutMs);
}
