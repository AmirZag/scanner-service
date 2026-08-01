using System.Runtime.InteropServices;
using NAPS2.Scan;
using ScannerService.Domain.Common;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Factory for creating scanner driver lists based on the current platform.
/// Encapsulates platform-specific driver logic.
/// </summary>
internal static class ScannerDriverFactory
{
    /// <summary>
    /// Gets the list of available scanner drivers for the current platform.
    /// </summary>
    public static List<Driver> GetAvailableDrivers()
    {
        var drivers = new List<Driver>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            drivers.Add(Driver.Twain);
            drivers.Add(Driver.Wia);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            drivers.Add(Driver.Sane);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            drivers.Add(Driver.Twain);
        }

        drivers.Add(Driver.Escl);

        return drivers;
    }

    /// <summary>
    /// Checks if a driver should be skipped based on initialization failures.
    /// </summary>
    public static bool ShouldSkipDriver(Driver driver, bool twainWorkerFailed)
    {
        return driver == Driver.Twain && twainWorkerFailed;
    }
}
