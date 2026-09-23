using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Provides cached access to scanner operations.
/// Caches expensive operations like scanner list retrieval to improve performance.
/// A single-flight gate prevents concurrent requests from each starting their own device
/// enumeration, and the last known list is served when a refresh fails outright.
/// </summary>
public sealed class CachedScannerService : IScannerQueries, IDisposable
{
    private readonly IScannerQueries _innerService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedScannerService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private List<ScannerDto>? _lastGoodList;

    // Cache durations
    private static readonly TimeSpan ScannerListCacheDuration = TimeSpan.FromSeconds(30);

    // Cache keys
    private const string ScannerListCacheKey = "ScannerList";

    public CachedScannerService(
        IScannerQueries innerService,
        IMemoryCache cache,
        ILogger<CachedScannerService> logger)
    {
        _innerService = innerService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        if (_cache.TryGetValue<List<ScannerDto>>(ScannerListCacheKey, out var cachedList))
        {
            _logger.LogDebug("Returning cached scanner list");
            return cachedList;
        }

        // Single-flight: waiters re-check the cache once the refresh completes instead of each
        // starting their own (potentially slow) device enumeration.
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<List<ScannerDto>>(ScannerListCacheKey, out cachedList))
            {
                _logger.LogDebug("Returning scanner list refreshed by a concurrent request");
                return cachedList;
            }

            // Not in cache, fetch from inner service
            _logger.LogDebug("Fetching scanner list from inner service");
            var scannerList = await _innerService.GetScannersListAsync(cancellationToken);

            // Cache the result. Partial (degraded) lists are cached deliberately: with the driver
            // cool-down in place they are stable across the TTL instead of re-triggering timeouts.
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ScannerListCacheDuration)
                .SetSize(1); // Each entry counts as 1 unit

            _cache.Set(ScannerListCacheKey, scannerList, cacheEntryOptions);
            _lastGoodList = scannerList;
            _logger.LogDebug("Cached scanner list for {Duration} seconds", ScannerListCacheDuration.TotalSeconds);

            return scannerList;
        }
        catch (OperationCanceledException)
        {
            // The requesting client is gone; never cache or substitute a result.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scanner list refresh failed; serving the last known list");
            return _lastGoodList ?? new List<ScannerDto>();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Clears the scanner list cache. Call this when the scanner configuration may have changed.
    /// </summary>
    public void ClearScannerListCache()
    {
        _logger.LogDebug("Clearing scanner list cache");
        _cache.Remove(ScannerListCacheKey);
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
    }
}
