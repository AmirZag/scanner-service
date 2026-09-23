namespace ScannerService.Domain.Common;

/// <summary>
/// Centralized constants for scanner operations.
/// Eliminates magic strings and improves type safety.
/// </summary>
public static class ScannerConstants
{
    /// <summary>Scanner paper sources</summary>
    public static class PaperSource
    {
        public const string Feeder = "Feeder";
        public const string Glass = "Glass";
    }

    /// <summary>Scanner bit depth settings</summary>
    public static class BitDepth
    {
        public const string BlackAndWhite = "BlackAndWhite";
        public const string Grayscale = "Grayscale";
        public const string Color = "Color";
    }

    /// <summary>Export file formats</summary>
    public static class ExportFormat
    {
        public const string PDF = "PDF";
        public const string JPEG = "JPEG";
        public const string PNG = "PNG";
        public const string TIFF = "TIFF";
        public const string MultiPageTIFF = "MultiPageTIFF";
    }

    /// <summary>Page size options</summary>
    public static class PageSize
    {
        public const string A4 = "A4";
        public const string A5 = "A5";
        public const string Letter = "Letter";
        public const string Legal = "Legal";
    }

    /// <summary>Horizontal alignment options</summary>
    public static class HorizontalAlign
    {
        public const string Left = "Left";
        public const string Center = "Center";
        public const string Right = "Right";
    }

    /// <summary>Scale ratios</summary>
    public static class Scale
    {
        public const string OneToOne = "1:1";
        public const string HalfSize = "1:2";
        public const string QuarterSize = "1:4";
        public const string EighthSize = "1:8";
    }

    /// <summary>Scanner driver names</summary>
    public static class Driver
    {
        public const string Twain = "Twain";
        public const string Wia = "Wia";
        public const string Escl = "Escl";
        public const string Sane = "Sane";
    }

    /// <summary>Default timeouts for scanner operations (see <see cref="ScannerTimeouts"/>).</summary>
    public static class Timeouts
    {
        /// <summary>Per-driver device enumeration budget (WIA/TWAIN).</summary>
        public const int DefaultDriverTimeoutMs = 15000;

        /// <summary>ESCL mDNS search window; must stay below the ESCL driver budget.</summary>
        public const int DefaultEsclSearchTimeoutMs = 4000;

        /// <summary>Margin added on top of the ESCL search timeout.</summary>
        public const int EsclSearchMarginMs = 2000;

        /// <summary>Cool-down after a driver's first timeout.</summary>
        public const int DefaultDriverCooldownMs = 60000;

        /// <summary>Upper bound for the exponential driver cool-down.</summary>
        public const int DefaultDriverCooldownMaxMs = 600000;

        /// <summary>Wait for a busy scanner before failing a scan request.</summary>
        public const int DefaultScanQueueTimeoutMs = 5000;

        /// <summary>Overall cap for a single scan job.</summary>
        public const int DefaultScanOverallTimeoutMs = 600000;

        /// <summary>Watchdog re-armed after each scanned page.</summary>
        public const int DefaultScanNoProgressTimeoutMs = 120000;

        /// <summary>Grace period for the web host during shutdown.</summary>
        public const int DefaultShutdownTimeoutMs = 5000;
    }
}
