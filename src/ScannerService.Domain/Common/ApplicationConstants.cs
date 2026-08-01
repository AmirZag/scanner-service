namespace ScannerService.Domain.Common;

/// <summary>
/// Centralized constants for application-wide values.
/// Eliminates magic numbers and improves maintainability.
/// </summary>
public static class ApplicationConstants
{
    /// <summary>File size limits in bytes</summary>
    public static class FileSizes
    {
        public const int OneMegabyte = 1048576;
        public const int OneHundredMegabytes = 104857600;
    }

    /// <summary>Rate limiting configuration</summary>
    public static class RateLimit
    {
        public const int DefaultMaxRequests = 100;
        public const int DefaultWindowMinutes = 1;
        public const int CleanupThreshold = 1000;
    }

    /// <summary>Recent scanning limits</summary>
    public static class RecentScans
    {
        public const int MaxDepth = 3;
        public const int MaxFiles = 1000;
    }

    /// <summary>Supported file extensions for scan files</summary>
    public static class SupportedExtensions
    {
        public static readonly string[] ScanFiles = { ".pdf", ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp" };
    }

    /// <summary>Port configuration</summary>
    public static class Ports
    {
        public const int MaxPortSearchRange = 100;
    }

    /// <summary>Export settings defaults</summary>
    public static class ExportDefaults
    {
        public const int DefaultImageQuality = 85;
        public const int DefaultResolution = 200;
        public const string DefaultFormat = "PDF";
        public const string DefaultFileName = "scan_{datetime}";
    }

    /// <summary>Profile defaults</summary>
    public static class ProfileDefaults
    {
        public const string DefaultPaperSource = "Glass";
        public const string DefaultBitDepth = "Color";
        public const string DefaultPageSize = "A4";
        public const string DefaultHorizontalAlign = "Center";
        public const int DefaultResolution = 200;
        public const string DefaultScale = "1:1";
        public const int DefaultBrightness = 0;
        public const int DefaultContrast = 0;
    }

    /// <summary>Database constants</summary>
    public static class Database
    {
        public const int DefaultExportSettingId = 1;
        public const int MaxNameLength = 100;
    }
}
