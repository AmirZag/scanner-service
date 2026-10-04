using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using ScannerService.Domain.Common;
using ScannerService.TrayApp.Middleware;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Drives RateLimitMiddleware directly (no server): the test owns the middleware instance, its
/// cache and its per-IP lock table, so the 100-request cleanup boundary and the expired-lock
/// eviction (which disposes the AsyncLock) can be observed through the private state. The lock
/// staleness is manufactured by backdating AsyncLock.LastUsed through its private setter.
/// </summary>
public sealed class RateLimitMiddlewareEvictionTests
{
    private static Task CompletedNext(HttpContext context)
    {
        return Task.CompletedTask;
    }

    private static RateLimitMiddleware CreateMiddleware()
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        return new RateLimitMiddleware(
            CompletedNext,
            new RateLimitOptions
            {
                MaxRequests = ApplicationConstants.RateLimit.DefaultMaxRequests,
                Window = TimeSpan.FromMinutes(10)
            },
            cache);
    }

    private static ConcurrentDictionary<string, AsyncLock> ReadLockTable(RateLimitMiddleware middleware)
    {
        FieldInfo locksField = typeof(RateLimitMiddleware).GetField("_ipLocks", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Private field _ipLocks not found");
        return (ConcurrentDictionary<string, AsyncLock>)locksField.GetValue(middleware)!;
    }

    private static void BackdateLastUsed(AsyncLock lockEntry, TimeSpan age)
    {
        PropertyInfo lastUsedProperty = typeof(AsyncLock).GetProperty("LastUsed", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Property LastUsed not found");
        lastUsedProperty.SetValue(lockEntry, DateTime.UtcNow - age);
    }

    [Fact]
    public async Task Invoke_FirstHundredRequests_RunCleanupWithoutEvicting()
    {
        RateLimitMiddleware middleware = CreateMiddleware();
        HttpContext request = new DefaultHttpContext();
        ConcurrentDictionary<string, AsyncLock> locks = ReadLockTable(middleware);

        for (int requestIndex = 0; requestIndex < ApplicationConstants.RateLimit.DefaultMaxRequests; requestIndex++)
        {
            await middleware.InvokeAsync(request);
        }

        AsyncLock lockEntry = Assert.Single(locks.Values);
        Assert.False(lockEntry.LastUsed.Add(TimeSpan.FromMinutes(20)) < DateTime.UtcNow);
    }

    [Fact]
    public async Task Invoke_StaleLockAtCleanupBoundary_IsEvictedAndDisposed()
    {
        RateLimitMiddleware middleware = CreateMiddleware();
        HttpContext request = new DefaultHttpContext();
        ConcurrentDictionary<string, AsyncLock> locks = ReadLockTable(middleware);

        // The stale entry rides on a DIFFERENT key: any request through a key refreshes its own
        // lock's LastUsed after acquire, which would un-backdate it before the cleanup pass.
        AsyncLock staleLock = new AsyncLock();
        locks["ratelimit_198.51.100.7"] = staleLock;
        BackdateLastUsed(staleLock, TimeSpan.FromMinutes(30));

        for (int requestIndex = 0; requestIndex < ApplicationConstants.RateLimit.DefaultMaxRequests; requestIndex++)
        {
            await middleware.InvokeAsync(request);
        }

        // Request 100's cleanup pass evicted (and disposed) the stale lock while keeping the
        // fresh one for the request key.
        Assert.False(locks.ContainsKey("ratelimit_198.51.100.7"));
        AsyncLock freshLock = Assert.Single(locks.Values);
        Assert.NotSame(staleLock, freshLock);

        // A later request for the evicted key works by creating a fresh lock.
        HttpContext evictedKeyRequest = new DefaultHttpContext();
        evictedKeyRequest.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.7");
        await middleware.InvokeAsync(evictedKeyRequest);

        Assert.True(locks.ContainsKey("ratelimit_198.51.100.7"));
        Assert.Equal(2, locks.Count);
    }
}
