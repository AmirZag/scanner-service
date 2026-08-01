using System;
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
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly IMemoryCache _cache;

    public RateLimitMiddleware(RequestDelegate next, RateLimitOptions options, IMemoryCache cache)
    {
        _next = next;
        _options = options;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIp(context);

        // Get or create the rate limit counter for this IP
        var counterKey = $"ratelimit_{clientIp}";

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

            // Increment the counter
            _cache.Set(counterKey, counter.Increment(), counter.GetExpiration(_options.Window));
        }
        else
        {
            // Add new counter with automatic expiration
            _cache.Set(counterKey, new RateLimitCounter(1, DateTime.UtcNow), DateTimeOffset.UtcNow.Add(_options.Window));
        }

        await _next(context);
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

    public DateTimeOffset GetExpiration(TimeSpan window)
    {
        // Set expiration to the end of the current window
        return WindowStart.Add(window);
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
    public int MaxRequests { get; set; } = Domain.Common.ApplicationConstants.RateLimit.DefaultMaxRequests;

    /// <summary>
    /// Time window for rate limiting
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(Domain.Common.ApplicationConstants.RateLimit.DefaultWindowMinutes);
}
