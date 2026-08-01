using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.Scan;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.Infrastructure.Services;

public class ScannerService : IScannerQueries, IScannerService, IAsyncDisposable
{
    private ScanningContext? _context;
    private ScanController? _controller;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<ScannerService> _logger;
    private bool _initialized;
    private bool _twainWorkerFailed;

    // Cached reflection field info for efficient bitmap extraction
    private static readonly System.Reflection.FieldInfo? CachedBitmapField = GetBitmapFieldInfo();

    public ScannerService(ILogger<ScannerService> logger)
    {
        _logger = logger;
    }

    private async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }
        await _lock.WaitAsync();

        try
        {
            if (_initialized)
            {
                return;
            }
            _logger.LogInformation("Initializing scanner context");

            ImageContext imageContext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new NAPS2.Images.Gdi.GdiImageContext()
                : new NAPS2.Images.ImageSharp.ImageSharpImageContext();

            _context = new ScanningContext(imageContext);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _context.SetUpWin32Worker();
                    _logger.LogInformation("TWAIN Worker initialized successfully");
                }
                catch (Exception ex)
                {
                    _twainWorkerFailed = true;
                    _logger.LogWarning(ex, "TWAIN worker setup failed. TWAIN scanning will be unavailable, but WIA and ESCL will work normally");
                }
            }

            _controller = new ScanController(_context);
            _initialized = true;
            _logger.LogInformation("Scanner initialization complete");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<ScannerDto>> GetScannersListAsync()
    {
        _logger.LogDebug("Retrieving scanner list");

        await InitializeAsync();

        var drivers = GetDrivers();

        // Query drivers in parallel for better performance
        var driverTasks = drivers.Select(async driver =>
        {
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                _logger.LogDebug("Skipping TWAIN driver due to worker initialization failure");
                return (Scanners: new List<ScannerDto>(), Driver: driver);
            }

            try
            {
                if (_controller == null)
                {
                    await InitializeAsync();
                }
                var controller = _controller ?? throw new InvalidOperationException("Controller not initialized");
                var devices = await controller.GetDeviceList(driver);
                var scannerDtos = devices.Select(d => new ScannerDto(d.ID, d.Name, driver.ToString())).ToList();
                _logger.LogDebug("Found {Count} devices for driver {Driver}", devices.Count, driver);
                return (Scanners: scannerDtos, Driver: driver);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get devices for driver {Driver}", driver);
                return (Scanners: new List<ScannerDto>(), Driver: driver);
            }
        }).ToList();

        var results = await Task.WhenAll(driverTasks);

        var scanners = new List<ScannerDto>();
        foreach (var (driverScanners, _) in results)
        {
            scanners.AddRange(driverScanners);
        }

        _logger.LogInformation("Found {TotalCount} total scanners across all drivers", scanners.Count);
        return scanners;
    }

    private static List<Driver> GetDrivers()
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

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing scanner provider");

        _context?.Dispose();
        _lock.Dispose();

        await Task.CompletedTask;
    }

    public async Task<ScanExecutionResult> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration)
    {
        var scanStartTime = DateTime.UtcNow;
        _logger.LogInformation("Starting scan operation - DeviceId: {DeviceId}, Format: {Format}, Resolution: {Resolution}",
            scanJobConfiguration.DeviceId, scanJobConfiguration.Format, scanJobConfiguration.Resolution);

        await InitializeAsync();

        var device = await FindDeviceAsync(scanJobConfiguration.DeviceId);

        if (device == null)
        {
            _logger.LogError("Scanner device not found - DeviceId: {DeviceId}", scanJobConfiguration.DeviceId);
            return ScanExecutionResult.Fail($"Scanner not found: {scanJobConfiguration.DeviceId}");
        }

        var options = new NAPS2.Scan.ScanOptions
        {
            Device = device,
            Dpi = scanJobConfiguration.Resolution,
            BitDepth = scanJobConfiguration.BitDepth switch
            {
                "BlackAndWhite" => BitDepth.BlackAndWhite,
                "Grayscale" => BitDepth.Grayscale,
                _ => BitDepth.Color
            },
            PaperSource = scanJobConfiguration.PaperSource.Equals("Feeder", StringComparison.OrdinalIgnoreCase)
            ? NAPS2.Scan.PaperSource.Feeder
            : NAPS2.Scan.PaperSource.Flatbed
        };

        _logger.LogDebug("Scan options configured - Dpi: {Dpi}, BitDepth: {BitDepth}, PaperSource: {PaperSource}",
            options.Dpi, scanJobConfiguration.BitDepth, options.PaperSource);

        var images = new List<ProcessedImage>();

        try
        {
            if (_controller == null)
            {
                await InitializeAsync();
            }
            var controller = _controller ?? throw new InvalidOperationException("Controller not initialized");
            await foreach (var image in controller.Scan(options))
            {
                images.Add(image);
                _logger.LogDebug("Captured image {ImageNumber} at {Timestamp}", images.Count, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Error during scan operation - Duration: {DurationMs}ms", duration.TotalMilliseconds);

            // Dispose any images that were captured before the error
            foreach (var img in images)
            {
                img.Dispose();
            }

            return ScanExecutionResult.Fail($"Scan operation failed: {ex.Message}");
        }

        if (images.Count == 0)
        {
            var duration = DateTime.UtcNow - scanStartTime;
            _logger.LogWarning("No images were scanned - Duration: {DurationMs}ms", duration.TotalMilliseconds);
            return ScanExecutionResult.Fail("No images were scanned. Please ensure the document is properly placed in the scanner and try again.");
        }

        _logger.LogInformation("Scanned {ImageCount} images, saving as {Format}", images.Count, scanJobConfiguration.Format);

        List<string> files;
        try
        {
            files = await SaveAsync(images, scanJobConfiguration);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Failed to save scanned images - Duration: {DurationMs}ms", duration.TotalMilliseconds);
            return ScanExecutionResult.Fail($"Failed to save scanned images: {ex.Message}");
        }
        finally
        {
            // Always dispose images, even on success
            foreach (var img in images)
            {
                img.Dispose();
            }
        }

        var totalDuration = DateTime.UtcNow - scanStartTime;
        _logger.LogInformation("Scan completed successfully - Images: {ImageCount}, Files: {FileCount}, Duration: {DurationMs}ms, OutputPath: {OutputPath}",
            images.Count, files.Count, totalDuration.TotalMilliseconds, scanJobConfiguration.ExportPath);
        return ScanExecutionResult.Succeed(files);
    }

    private async Task<List<string>> SaveAsync(List<ProcessedImage> images, ScanJobConfiguration scanJobConfiguration)
    {
        var files = new List<string>();
        var name = scanJobConfiguration.FileName.Replace("{datetime}", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

        if (scanJobConfiguration.Format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {

            var path = Path.Combine(scanJobConfiguration.ExportPath, $"{name}.pdf");
            await new PdfExporter(_context!).Export(path, images);
            files.Add(path);
            _logger.LogDebug("Saved PDF: {Path}", path);
        }
        else if (scanJobConfiguration.Format.Equals("MultiPageTIFF", StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(scanJobConfiguration.ExportPath, $"{name}.tiff");
            await SaveMultiPageTiffAsync(images, path);
            files.Add(path);
            _logger.LogDebug("Saved multi-page TIFF: {Path}", path);
        }
        else
        {
            var format = scanJobConfiguration.Format.ToLowerInvariant() switch
            {
                "png" => ImageFileFormat.Png,
                "tiff" => ImageFileFormat.Tiff,
                _ => ImageFileFormat.Jpeg
            };

            for (int i = 0; i < images.Count; i++)
            {
                var ext = scanJobConfiguration.Format.ToLowerInvariant();
                var path = Path.Combine(scanJobConfiguration.ExportPath, $"{name}_{i + 1}.{ext}");
                images[i].Save(path, format);
                files.Add(path);
            }
            _logger.LogDebug("Saved {Count} {Format} files", files.Count, scanJobConfiguration.Format);
        }

        return files;
    }

    private async Task SaveMultiPageTiffAsync(List<ProcessedImage> images, string outputPath)
    {
        await Task.Run(() =>
        {
            var tiffEncoder = GetTiffEncoder();
            using var firstBitmap = GetBitmapFromImage(images[0]);
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);

            firstBitmap.Save(outputPath, tiffEncoder, encoderParams);
            encoderParams.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionPage);

            for (int i = 1; i < images.Count; i++)
            {
                using var bitmap = GetBitmapFromImage(images[i]);
                firstBitmap.SaveAdd(bitmap, encoderParams);
            }
#pragma warning disable S4143 // Reusing encoderParams with different parameter values is intentional
            encoderParams.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
#pragma warning restore S4143
            firstBitmap.SaveAdd(encoderParams);
        });
    }

    private static ImageCodecInfo GetTiffEncoder()
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == ImageFormat.Tiff.Guid)
            ?? throw new InvalidOperationException("TIFF encoder not found");
    }

    /// <summary>
    /// Extracts a Bitmap from a ProcessedImage.
    /// First attempts to use the cached reflection field info (if available).
    /// Falls back to temp file approach if reflection fails or is not available.
    /// </summary>
    private static Bitmap GetBitmapFromImage(ProcessedImage image)
    {
        // Try using cached reflection field info (optimized path)
        if (CachedBitmapField != null)
        {
            try
            {
                if (CachedBitmapField.GetValue(image) is Bitmap bitmap)
                {
                    // Clone the bitmap to avoid ownership issues
                    return new Bitmap(bitmap);
                }
            }
            catch
            {
                // Reflection failed, fall through to temp file approach
            }
        }

        // Fallback: Use temp file approach (reliable but slower)
        return GetBitmapViaTempFile(image);
    }

    /// <summary>
    /// Extracts a Bitmap from ProcessedImage by saving to a temporary file.
    /// This is the reliable fallback method that works regardless of NAPS2 internals.
    /// </summary>
    private static Bitmap GetBitmapViaTempFile(ProcessedImage image)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.bmp");
        try
        {
            image.Save(tempPath, ImageFileFormat.Bmp);
            return new Bitmap(tempPath);
        }
        catch (Exception ex) when (IsCriticalException(ex))
        {
            // Re-throw critical exceptions
            throw;
        }
        catch
        {
            // If temp file approach fails, try one more time with a different path
            var fallbackPath = Path.Combine(Path.GetTempPath(), $"scan_fallback_{Guid.NewGuid()}.bmp");
            try
            {
                image.Save(fallbackPath, ImageFileFormat.Bmp);
                return new Bitmap(fallbackPath);
            }
            finally
            {
                // Clean up fallback file
                if (File.Exists(fallbackPath))
                {
                    try { File.Delete(fallbackPath); }
                    catch { /* Ignore cleanup errors */ }
                }
            }
            throw;
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// Safely retrieves the bitmap field info from ProcessedImage type using reflection.
    /// Returns null if the field cannot be found (e.g., NAPS2 version changed).
    /// The result is cached to avoid repeated reflection overhead.
    /// Note: Reflection is used here as an optimization over the temp file fallback.
    /// The code gracefully handles reflection failures by falling back to temp file approach.
    /// </summary>
#pragma warning disable S3011 // Reflection required for NAPS2 interoperability - has safe fallback
    private static System.Reflection.FieldInfo? GetBitmapFieldInfo()
    {
        try
        {
            var imageType = typeof(ProcessedImage);
            var field = imageType.GetField("_bitmap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field;
        }
        catch
        {
            // If reflection fails (e.g., obfuscated assembly, changed internals), return null
            // The code will fall back to temp file approach
            return null;
        }
    }
#pragma warning restore S3011

    private static bool IsCriticalException(Exception ex)
    {
        return ex is OutOfMemoryException or AccessViolationException or StackOverflowException;
    }

    private async Task<ScanDevice?> FindDeviceAsync(string deviceId)
    {
        var drivers = GetDrivers();

        // Query drivers in parallel, but return as soon as we find the device
        var driverTasks = drivers.Select(async driver =>
        {
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                return (Device: (ScanDevice?)null, Driver: driver);
            }

            try
            {
                if (_controller == null)
                {
                    await InitializeAsync();
                }
                var controller = _controller ?? throw new InvalidOperationException("Controller not initialized");
                var devices = await controller.GetDeviceList(driver);
                var device = devices.FirstOrDefault(d => d.ID == deviceId);
                return (Device: device, Driver: driver);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error searching for device in driver {Driver}", driver);
                return (Device: (ScanDevice?)null, Driver: driver);
            }
        }).ToList();

        var results = await Task.WhenAll(driverTasks);

        // Return first non-null device
        foreach (var (device, _) in results)
        {
            if (device != null)
            {
                return device;
            }
        }

        return null;
    }
}
