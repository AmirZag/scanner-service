using System;
using ScannerService.Application.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class TimeProviderTests
{
    [Fact]
    public void SystemTimeProvider_ReturnsTheRealClock_WithCorrectKinds()
    {
        SystemTimeProvider provider = new SystemTimeProvider();
        DateTime beforeUtc = DateTime.UtcNow;
        DateTime beforeLocal = DateTime.Now;

        DateTime utcNow = provider.UtcNow;
        DateTime localNow = provider.Now;

        DateTime afterUtc = DateTime.UtcNow;
        DateTime afterLocal = DateTime.Now;

        Assert.Equal(DateTimeKind.Utc, utcNow.Kind);
        Assert.Equal(DateTimeKind.Local, localNow.Kind);
        Assert.InRange(utcNow, beforeUtc, afterUtc);
        Assert.InRange(localNow, beforeLocal, afterLocal);
    }

    [Fact]
    public void TestTimeProvider_DefaultConstructor_StartsAtTheRealUtcClock()
    {
        DateTime before = DateTime.UtcNow;

        TestTimeProvider provider = new TestTimeProvider();

        DateTime after = DateTime.UtcNow;

        Assert.Equal(DateTimeKind.Utc, provider.UtcNow.Kind);
        Assert.InRange(provider.UtcNow, before, after);
    }

    [Fact]
    public void TestTimeProvider_ExposesTheInjectedTime_ForBothUtcAndLocalReaders()
    {
        DateTime frozen = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        TestTimeProvider provider = new TestTimeProvider(frozen);

        Assert.Equal(frozen, provider.UtcNow);
        Assert.Equal(frozen.ToLocalTime(), provider.Now);
        Assert.Equal(DateTimeKind.Local, provider.Now.Kind);
    }

    [Fact]
    public void TestTimeProvider_Advance_MovesTheFrozenClockForward()
    {
        TestTimeProvider provider = new TestTimeProvider(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        provider.Advance(TimeSpan.FromMinutes(90));

        Assert.Equal(new DateTime(2026, 3, 15, 13, 30, 0, DateTimeKind.Utc), provider.UtcNow);
    }

    [Fact]
    public void TestTimeProvider_SetTime_ReplacesTheFrozenClock()
    {
        TestTimeProvider provider = new TestTimeProvider(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        provider.SetTime(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), provider.UtcNow);
    }
}
