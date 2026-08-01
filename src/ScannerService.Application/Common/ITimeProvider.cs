namespace ScannerService.Application.Common;

/// <summary>
/// Abstraction for time-related operations.
/// Enables testing and consistent time handling across the application.
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current local time.
    /// </summary>
    DateTime Now { get; }
}

/// <summary>
/// Default implementation using system time.
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
}

/// <summary>
/// Time provider for testing that allows time control.
/// </summary>
public class TestTimeProvider : ITimeProvider
{
    public DateTime CurrentTime { get; private set; }

    public TestTimeProvider(DateTime? startTime = null)
    {
        CurrentTime = startTime ?? DateTime.UtcNow;
    }

    public DateTime UtcNow => CurrentTime;
    public DateTime Now => CurrentTime.ToLocalTime();

    /// <summary>
    /// Advances time by a specified amount.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        CurrentTime = CurrentTime.Add(amount);
    }

    /// <summary>
    /// Sets the time to a specific point.
    /// </summary>
    public void SetTime(DateTime time)
    {
        CurrentTime = time;
    }
}
