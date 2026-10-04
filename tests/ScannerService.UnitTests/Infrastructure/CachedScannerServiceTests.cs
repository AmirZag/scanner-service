using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tests for the <see cref="CachedScannerService"/> decorator: cache hit/miss behavior,
/// single-flight enumeration, last-good-list on failure, cancellation rethrow, cache
/// invalidation via <see cref="IScannerListCache"/>, and disposal. Uses a real
/// MemoryCache and hand-rolled inner fakes; no scanner drivers are touched.
/// </summary>
public sealed class CachedScannerServiceTests
{
    [Fact]
    public async Task GetScannersListAsync_FirstCall_EnumeratesInnerAndCaches()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        List<ScannerDto> result = await decorator.GetScannersListAsync();

        Assert.Equal(1, inner.CallCount);
        Assert.Equal(2, result.Count);
        Assert.Equal("fake-scanner-1", result[0].Id);
        Assert.Equal("fake-scanner-2", result[1].Id);
    }

    [Fact]
    public async Task GetScannersListAsync_SecondCall_ServedFromCacheAsSameInstance()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        List<ScannerDto> first = await decorator.GetScannersListAsync();
        List<ScannerDto> second = await decorator.GetScannersListAsync();

        Assert.Equal(1, inner.CallCount);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetScannersListAsync_FiveConcurrentFirstCalls_EnumeratesInnerExactlyOnce()
    {
        GatedScannerQueriesFake inner = new GatedScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        List<Task<List<ScannerDto>>> calls = Enumerable
            .Range(0, 5)
            .Select(_ => Task.Run(() => decorator.GetScannersListAsync()))
            .ToList();

        await inner.WaitUntilEnumeratingAsync().WaitAsync(TimeSpan.FromSeconds(10));
        inner.Release();

        List<ScannerDto>[] results = await Task.WhenAll(calls);

        Assert.Equal(1, inner.CallCount);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.All(results, result => Assert.Equal("gated-scanner", result[0].Id));
    }

    [Fact]
    public async Task GetScannersListAsync_WaiterQueuedDuringEnumeration_ServesRefreshedListFromDoubleCheck()
    {
        // Pins the post-gate re-check path (the "refreshed by a concurrent request" branch).
        // Both callers are invoked directly on the test thread: caller 1's synchronous prefix
        // runs inline and it parks inside the gated inner enumeration holding the single-flight
        // gate; caller 2's synchronous prefix (cache miss, WaitAsync on the held gate) also runs
        // inline, so the waiter is deterministically parked before the enumeration completes.
        // The Task.Run-based overlap test cannot guarantee that under thread-pool starvation,
        // which left this branch uncovered on constrained runners.
        GatedScannerQueriesFake inner = new GatedScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        Task<List<ScannerDto>> first = decorator.GetScannersListAsync();
        await inner.WaitUntilEnumeratingAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Task<List<ScannerDto>> second = decorator.GetScannersListAsync();
        Assert.False(second.IsCompleted);

        inner.Release();

        List<ScannerDto> firstResult = await first;
        List<ScannerDto> secondResult = await second;

        Assert.Equal(1, inner.CallCount);
        Assert.Same(firstResult, secondResult);
    }

    [Fact]
    public async Task GetScannersListAsync_InnerFailureAfterCachedValue_ServesLastGoodList()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        List<ScannerDto> first = await decorator.GetScannersListAsync();
        decorator.ClearScannerListCache();
        inner.FailureToThrow = new InvalidOperationException("driver exploded");

        List<ScannerDto> second = await decorator.GetScannersListAsync();

        Assert.Equal(2, inner.CallCount);
        Assert.Same(first, second);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public async Task GetScannersListAsync_InnerFailureWithoutCachedValue_ReturnsEmptyList()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);
        inner.FailureToThrow = new InvalidOperationException("driver exploded");

        List<ScannerDto> result = await decorator.GetScannersListAsync();

        Assert.Equal(1, inner.CallCount);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetScannersListAsync_InnerOperationCanceled_RethrowsToCaller()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);
        inner.FailureToThrow = new OperationCanceledException("client gone");

        await Assert.ThrowsAsync<OperationCanceledException>(() => decorator.GetScannersListAsync());

        // The canceled result must never have been cached: a second call enumerates again.
        await Assert.ThrowsAsync<OperationCanceledException>(() => decorator.GetScannersListAsync());

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task ClearScannerListCache_AfterCachedValue_ForcesReEnumeration()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        List<ScannerDto> first = await decorator.GetScannersListAsync();
        decorator.ClearScannerListCache();
        List<ScannerDto> second = await decorator.GetScannersListAsync();

        Assert.Equal(2, inner.CallCount);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetScannersListAsync_ClearWhileEnumerating_ResultIsNotCached()
    {
        // The inner enumeration blocks on a gate, so the clear lands after the generation was
        // read and before the result is examined: the fresh list must not enter the cache and
        // the next request must re-enumerate.
        GatedScannerQueriesFake inner = new GatedScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        Task<List<ScannerDto>> inFlightEnumeration = decorator.GetScannersListAsync();
        decorator.ClearScannerListCache();
        inner.Release();

        List<ScannerDto> first = await inFlightEnumeration;
        List<ScannerDto> second = await decorator.GetScannersListAsync();

        Assert.Equal("gated-scanner", first[0].Id);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal("gated-scanner", second[0].Id);
    }

    [Fact]
    public async Task ClearScannerListCache_BeforeFirstCall_StillCachesAfterSingleEnumeration()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        using CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        decorator.ClearScannerListCache();

        List<ScannerDto> first = await decorator.GetScannersListAsync();
        List<ScannerDto> second = await decorator.GetScannersListAsync();

        Assert.Equal(1, inner.CallCount);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetScannersListAsync_AfterDispose_StillServesCachedListWithoutReEnumerating()
    {
        CountingScannerQueriesFake inner = new CountingScannerQueriesFake();
        using MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        CachedScannerService decorator = new CachedScannerService(inner, cache, NullLogger<CachedScannerService>.Instance);

        try
        {
            List<ScannerDto> first = await decorator.GetScannersListAsync();

            decorator.Dispose();

            List<ScannerDto> second = await decorator.GetScannersListAsync();

            Assert.Equal(1, inner.CallCount);
            Assert.Same(first, second);
        }
        finally
        {
            decorator.Dispose();
        }
    }
}
