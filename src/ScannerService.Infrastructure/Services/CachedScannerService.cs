using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Provides cached access to scanner operations.
/// Caches expensive operations like scanner list retrieval to improve performance.
/// </summary>
public sealed class CachedScannerService : IScannerQueries
{
    private readonly IScannerQueries _innerService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedScannerService> _logger;

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

        // Not in cache, fetch from inner service
        _logger.LogDebug("Fetching scanner list from inner service");
        var scannerList = await _innerService.GetScannersListAsync(cancellationToken);

        // Cache the result
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ScannerListCacheDuration)
            .SetSize(1); // Each entry counts as 1 unit

        _cache.Set(ScannerListCacheKey, scannerList, cacheEntryOptions);
        _logger.LogDebug("Cached scanner list for {Duration} seconds", ScannerListCacheDuration.TotalSeconds);

        return scannerList;
    }

    /// <summary>
    /// Clears the scanner list cache. Call this when the scanner configuration may have changed.
    /// </summary>
    public void ClearScannerListCache()
    {
        _logger.LogDebug("Clearing scanner list cache");
        _cache.Remove(ScannerListCacheKey);
    }
}
