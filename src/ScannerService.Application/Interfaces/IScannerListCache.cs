namespace ScannerService.Application.Interfaces;

/// <summary>
/// Provides cache invalidation for the cached scanner device list.
/// </summary>
public interface IScannerListCache
{
    /// <summary>
    /// Clears the cached scanner device list so the next request re-enumerates devices.
    /// Call this after scanner or network configuration changes.
    /// </summary>
    void ClearScannerListCache();
}
