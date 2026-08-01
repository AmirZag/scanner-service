using System;
using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Serilog;

namespace ScannerService.TrayApp.Middleware;

/// <summary>
/// Simple rate limiting middleware to prevent API abuse.
/// Limits requests per IP address within a time window.
/// Uses IMemoryCache for automatic expiration and cleanup.
/// Thread-safe using AsyncLock per IP key.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, AsyncLock> _ipLocks;
    private readonly TimeSpan _lockExpiration;

    public RateLimitMiddleware(RequestDelegate next, RateLimitOptions options, IMemoryCache cache)
    {
        _next = next;
        _options = options;
        _cache = cache;
        _ipLocks = new ConcurrentDictionary<string, AsyncLock>();
        _lockExpiration = TimeSpan.FromMinutes(_options.Window.TotalMinutes * 2);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIp(context);
        var counterKey = $"ratelimit_{clientIp}";

        // Get or create a lock for this specific IP
        var ipLock = _ipLocks.GetOrAdd(counterKey, _ => new AsyncLock());

        try
        {
            await ipLock.AcquireAsync(context.RequestAborted);

            if (_cache.TryGetValue<RateLimitCounter>(counterKey, out var counter) && counter != null)
            {
                if (counter.Count >= _options.MaxRequests)
                {
                    // Rate limit exceeded - calculate retry-after
                    var retryAfter = Math.Ceiling((_options.Window - (DateTime.UtcNow - counter.WindowStart)).TotalSeconds);
                    LogWarning(context, clientIp, counter.Count);

                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.Append("Retry-After", retryAfter.ToString("F0", CultureInfo.InvariantCulture));
                    await context.Response.WriteAsync("Rate limit exceeded. Please try again later.", context.RequestAborted);
                    return;
                }

                // Increment the counter (under lock, so atomic)
                counter.Increment();
                _cache.Set(counterKey, counter, counter.GetExpiration(_options.Window));
            }
            else
            {
                // Add new counter with automatic expiration (under lock, so atomic)
                _cache.Set(counterKey, new RateLimitCounter(1, DateTime.UtcNow), DateTimeOffset.UtcNow.Add(_options.Window));
            }
        }
        finally
        {
            ipLock.Release();

            // Clean up expired locks periodically (every 100 requests to avoid overhead)
            if (counterKey.GetHashCode() % 100 == 0)
            {
                CleanupExpiredLocks();
            }
        }

        await _next(context);
    }

    private void CleanupExpiredLocks()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _ipLocks)
        {
            if (kvp.Value.LastUsed.Add(_lockExpiration) < now &&
                _ipLocks.TryRemove(kvp.Key, out var lockObj))
            {
                lockObj.Dispose();
            }
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        // Try to get IP from X-Forwarded-For header first (for proxy scenarios)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && forwardedFor.Count > 0)
        {
            return forwardedFor.ToString();
        }

        // Fall back to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static void LogWarning(HttpContext context, string clientIp, int requestCount)
    {
        Log.Warning("Rate limit exceeded - IP: {ClientIp}, Requests: {RequestCount}, Path: {RequestPath}",
            clientIp, requestCount, context.Request.Path);
    }
}

/// <summary>
/// Async lock implementation for rate limiting coordination.
/// Properly disposable to prevent memory leaks.
/// </summary>
internal sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    public DateTime LastUsed { get; private set; }

    public AsyncLock()
    {
        _semaphore = new SemaphoreSlim(1, 1);
        LastUsed = DateTime.UtcNow;
    }

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        LastUsed = DateTime.UtcNow;
    }

    public void Release()
    {
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

/// <summary>
/// Tracks request count for a specific IP. Thread-safe.
/// </summary>
internal sealed class RateLimitCounter
{
    private readonly object _lock = new();

    public int Count { get; private set; }
    public DateTime WindowStart { get; }

    public RateLimitCounter(int count, DateTime windowStart)
    {
        Count = count;
        WindowStart = windowStart;
    }

    public DateTimeOffset GetExpiration(TimeSpan window)
    {
        // Set expiration to the end of the current window
        return WindowStart.Add(window);
    }

    public void Increment()
    {
        lock (_lock)
        {
            Count++;
        }
    }
}

/// <summary>
/// Configuration options for rate limiting
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// Maximum number of requests allowed per time window
    /// </summary>
    public int MaxRequests { get; set; } = Domain.Common.ApplicationConstants.RateLimit.DefaultMaxRequests;

    /// <summary>
    /// Time window for rate limiting
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(Domain.Common.ApplicationConstants.RateLimit.DefaultWindowMinutes);
}
