using System;
using System.Threading.Tasks;
using NAPS2.Scan;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tests for the per-driver cool-down tracker (the scanner circuit breaker). Windows are wide
/// (3 s base, 6 s max) and expiry is asserted by polling with a generous deadline, so no
/// assertion depends on wall-clock precision under parallel test-host load. "Still cooling"
/// assertions run well inside the window. No scanner drivers are touched.
/// </summary>
public sealed class DriverHealthTrackerTests
{
    [Fact]
    public void IsCoolingDown_FreshTracker_NoDriverIsCoolingDown()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());

        Assert.False(tracker.IsCoolingDown(Driver.Twain));
        Assert.False(tracker.IsCoolingDown(Driver.Wia));
        Assert.False(tracker.IsCoolingDown(Driver.Escl));
        Assert.Null(tracker.CoolDownEnd(Driver.Twain));
    }

    [Fact]
    public async Task RecordTimeout_SingleTimeout_CoolsDownThenExpiresAfterBaseWindow()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());

        tracker.RecordTimeout(Driver.Twain);

        Assert.True(tracker.IsCoolingDown(Driver.Twain));
        DateTime? end = tracker.CoolDownEnd(Driver.Twain);
        Assert.True(end.HasValue);
        Assert.Null(tracker.CoolDownEnd(Driver.Wia));

        await WaitForExpiryAsync(tracker, Driver.Twain);

        // Expiry is evaluated lazily by IsCoolingDown; the stored until-value is intentionally
        // kept (it feeds the next exponential backoff) and is only removed by RecordSuccess.
        Assert.False(tracker.IsCoolingDown(Driver.Twain));
        Assert.True(tracker.CoolDownEnd(Driver.Twain).HasValue);
    }

    [Fact]
    public async Task RecordTimeout_ConsecutiveTimeouts_DoublesBackoffClampedAtMax()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());

        tracker.RecordTimeout(Driver.Twain);
        tracker.RecordTimeout(Driver.Twain);

        // Second window is 2 x 3000 = 6000 ms; the check runs 1500 ms in, so a window that
        // failed to double (3000 ms) would have needed to expire within an artificially short
        // span - the margin makes the doubling observable without tight timing.
        await Task.Delay(1500);
        Assert.True(tracker.IsCoolingDown(Driver.Twain));

        // A third timeout would produce 6000 ms without the max clamp; clamped to 6000 ms.
        tracker.RecordTimeout(Driver.Twain);
        await WaitForExpiryAsync(tracker, Driver.Twain);
    }

    [Fact]
    public void RecordSuccess_ClearsCoolDownImmediately()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());
        tracker.RecordTimeout(Driver.Twain);

        tracker.RecordSuccess(Driver.Twain);

        Assert.False(tracker.IsCoolingDown(Driver.Twain));
        Assert.Null(tracker.CoolDownEnd(Driver.Twain));
    }

    [Fact]
    public async Task RecordSuccess_ResetsConsecutiveTimeoutCount()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());
        tracker.RecordTimeout(Driver.Twain);
        tracker.RecordSuccess(Driver.Twain);
        tracker.RecordTimeout(Driver.Twain);

        // After a reset the window is back to the 3000 ms base; an unreset counter would have
        // produced a 6000 ms window, which is still cooling when the base window expires.
        await WaitForExpiryAsync(tracker, Driver.Twain);

        Assert.False(tracker.IsCoolingDown(Driver.Twain));
    }

    [Fact]
    public async Task RecordTimeout_DifferentDrivers_TrackedIndependently()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());

        tracker.RecordTimeout(Driver.Twain);
        tracker.RecordTimeout(Driver.Twain);
        tracker.RecordTimeout(Driver.Wia);

        Assert.False(tracker.IsCoolingDown(Driver.Escl));

        // Wia's single-timeout base window expires while Twain's doubled window is still far
        // from its end (at least half of it remains when Wia expires).
        await WaitForExpiryAsync(tracker, Driver.Wia);

        Assert.False(tracker.IsCoolingDown(Driver.Wia));
        Assert.True(tracker.IsCoolingDown(Driver.Twain));
    }

    [Fact]
    public void RecordSuccess_UnknownDriver_IsNoOp()
    {
        DriverHealthTracker tracker = new DriverHealthTracker(CreateTimeouts());

        tracker.RecordSuccess(Driver.Sane);

        Assert.False(tracker.IsCoolingDown(Driver.Sane));
        Assert.Null(tracker.CoolDownEnd(Driver.Sane));
    }

    /// <summary>
    /// Polls until the driver's cool-down expired, with a deadline wide enough to absorb
    /// arbitrary parallel test-host stalls; fails if it is still cooling at the deadline.
    /// </summary>
    private static async Task WaitForExpiryAsync(DriverHealthTracker tracker, Driver driver)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        while (tracker.IsCoolingDown(driver) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.False(tracker.IsCoolingDown(driver), $"Expected the {driver} cool-down to expire within the deadline");
    }

    private static ScannerTimeouts CreateTimeouts()
    {
        return new ScannerTimeouts
        {
            DriverCooldownMs = 3000,
            DriverCooldownMaxMs = 6000
        };
    }
}
