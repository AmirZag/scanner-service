using System;
using System.Threading.Tasks;
using NAPS2.Scan;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tests for the per-driver cool-down tracker (the scanner circuit breaker). Uses tiny
/// deterministic windows (50 ms base, 100 ms max) with bounded delays of 30-110 ms so every
/// assertion stays coarse relative to the configured windows and never depends on wall-clock
/// precision. No scanner drivers are touched.
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

        // Bounded 600 ms wait: the first-timeout window is 500 ms, so it must have expired.
        await Task.Delay(600);

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

        // Second window is 2 x 500 = 1000 ms, so it is still cooling after a bounded 600 ms wait;
        // a window that failed to double (500 ms) would already have expired.
        await Task.Delay(600);
        Assert.True(tracker.IsCoolingDown(Driver.Twain));

        // A third timeout would produce 2000 ms without the max clamp; clamped to 1000 ms the
        // window expires within a bounded 1100 ms wait.
        tracker.RecordTimeout(Driver.Twain);
        await Task.Delay(1100);
        Assert.False(tracker.IsCoolingDown(Driver.Twain));
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

        // After a reset the window is back to the 500 ms base; an unreset counter would have
        // produced a 1000 ms window that is still cooling after a bounded 600 ms wait.
        await Task.Delay(600);

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

        // Bounded 600 ms wait: Wia's single-timeout 500 ms window has expired while Twain's
        // doubled 1000 ms window is still running.
        await Task.Delay(600);

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

    private static ScannerTimeouts CreateTimeouts()
    {
        // Generous windows: tight millisecond budgets make these tests flaky under parallel
        // test-host load; the assertions only need base < doubled <= clamped, well separated.
        return new ScannerTimeouts
        {
            DriverCooldownMs = 500,
            DriverCooldownMaxMs = 1000
        };
    }
}
