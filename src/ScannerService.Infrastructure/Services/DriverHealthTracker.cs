using System.Collections.Concurrent;
using NAPS2.Scan;
using ScannerService.Domain.Common;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Tracks per-driver responsiveness so an unresponsive driver (e.g. a TWAIN driver whose worker hangs, or WIA
/// talking to offline network devices) is skipped for a cool-down period instead of being re-queried on every
/// request. Cool-downs grow exponentially with consecutive timeouts and reset on success.
/// Owned by <see cref="ScannerService"/>; thread-safe.
/// </summary>
internal sealed class DriverHealthTracker
{
    private readonly ScannerTimeouts _timeouts;
    private readonly ConcurrentDictionary<Driver, int> _consecutiveTimeouts = new();
    private readonly ConcurrentDictionary<Driver, DateTime> _coolDownUntil = new();

    public DriverHealthTracker(ScannerTimeouts timeouts)
    {
        _timeouts = timeouts;
    }

    public bool IsCoolingDown(Driver driver) =>
        _coolDownUntil.TryGetValue(driver, out DateTime until) && DateTime.UtcNow < until;

    public DateTime? CoolDownEnd(Driver driver) =>
        _coolDownUntil.TryGetValue(driver, out DateTime until) ? until : null;

    public void RecordTimeout(Driver driver)
    {
        int failures = _consecutiveTimeouts.AddOrUpdate(driver, 1, (_, count) => count + 1);
        double backoff = Math.Min(
            _timeouts.DriverCooldownMs * Math.Pow(2, failures - 1),
            _timeouts.DriverCooldownMaxMs);
        _coolDownUntil[driver] = DateTime.UtcNow.AddMilliseconds(backoff);
    }

    public void RecordSuccess(Driver driver)
    {
        _consecutiveTimeouts.TryRemove(driver, out _);
        _coolDownUntil.TryRemove(driver, out _);
    }
}
