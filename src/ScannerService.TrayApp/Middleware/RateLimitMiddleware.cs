using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ScannerService.TrayApp.Middleware;

/// <summary>
/// Simple rate limiting middleware to prevent API abuse
/// Limits requests per IP address within a time window
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;

    // Track request counts per IP
    private readonly ConcurrentDictionary<string, RateLimitCounter> _counters = new();

    public RateLimitMiddleware(RequestDelegate next, RateLimitOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIp(context);

        // Check rate limit
        if (_counters.TryGetValue(clientIp, out var counter))
        {
            // Clean up old entries periodically
            CleanupCounters();

            if (counter.IsExpired(_options.Window))
            {
                // Reset counter if window expired
                _counters.TryUpdate(clientIp, new RateLimitCounter(1, DateTime.UtcNow), counter);
            }
            else if (counter.Count >= _options.MaxRequests)
            {
                // Rate limit exceeded
                LogWarning(context, clientIp, counter.Count);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.Append("Retry-After", Math.Ceiling((_options.Window - (DateTime.UtcNow - counter.WindowStart)).TotalSeconds).ToString("F0", CultureInfo.InvariantCulture));
                await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
                return;
            }
            else
            {
                // Increment counter
                _counters.TryUpdate(clientIp, counter.Increment(), counter);
            }
        }
        else
        {
            // Add new counter
            _counters.TryAdd(clientIp, new RateLimitCounter(1, DateTime.UtcNow));
        }

        await _next(context);
    }

    private static string GetClientIp(HttpContext context)
    {
        // Try to get IP from X-Forwarded-For header first (for proxy scenarios)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.ToString()!;
        }

        // Fall back to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupCounters()
    {
        // Only cleanup occasionally to avoid performance impact
        if (_counters.Count > 1000)
        {
            foreach (var kvp in _counters)
            {
                if (kvp.Value.IsExpired(_options.Window))
                {
                    _counters.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private static void LogWarning(HttpContext context, string clientIp, int requestCount)
    {
        Log.Warning("Rate limit exceeded - IP: {ClientIp}, Requests: {RequestCount}, Path: {RequestPath}",
            clientIp, requestCount, context.Request.Path);
    }
}

/// <summary>
/// Tracks request count for a specific IP
/// </summary>
internal sealed class RateLimitCounter
{
    public int Count { get; }
    public DateTime WindowStart { get; }

    public RateLimitCounter(int count, DateTime windowStart)
    {
        Count = count;
        WindowStart = windowStart;
    }

    public bool IsExpired(TimeSpan window)
    {
        return DateTime.UtcNow - WindowStart > window;
    }

    public RateLimitCounter Increment()
    {
        return new RateLimitCounter(Count + 1, WindowStart);
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
    public int MaxRequests { get; set; } = 60; // 60 requests per minute default

    /// <summary>
    /// Time window for rate limiting
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
