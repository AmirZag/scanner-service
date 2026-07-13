using System.Diagnostics.CodeAnalysis;
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
            _logger.LogInformation("Initilizing scanner context");

            ImageContext imageContext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new NAPS2.Images.Gdi.GdiImageContext()
                : new NAPS2.Images.ImageSharp.ImageSharpImageContext();

            _context = new ScanningContext(imageContext);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _context.SetUpWin32Worker();
                    _logger.LogInformation("TWAIN Worker initilized successfully");
                }
                catch (Exception ex)
                {
                    _twainWorkerFailed = true;
                    _logger.LogWarning(ex, "TWAIN worker setup failed. TWAIN scaninng will be unavailable, But WIA and ESCL will work normally");
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

        var scanners = new List<ScannerDto>();

        var drivers = GetDrivers();

        foreach (var driver in drivers)
        {
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                _logger.LogDebug("Skipping TWAIN driver due to worker initialization failure");
            }
            try
            {
                var devices = await _controller!.GetDeviceList(driver);
                scanners.AddRange(devices.Select(d => new ScannerDto(d.ID, d.Name, driver.ToString())));
                _logger.LogDebug("Found {Count} devices for driver {Driver}", devices.Count, driver);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get devices for driver {Driver}", driver);
            }
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
            await foreach (var image in _controller!.Scan(options))
            {
                images.Add(image);
                _logger.LogDebug("Captured image {ImageNumber} at {Timestamp}", images.Count, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Error during scan operation - Duration: {DurationMs}ms", duration.TotalMilliseconds);
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
            foreach (var img in images)
            {
                img.Dispose();
            }
            return ScanExecutionResult.Fail($"Failed to save scanned images: {ex.Message}");
        }

        foreach (var img in images)
        {
            img.Dispose();
        }

        var totalDuration = DateTime.UtcNow - scanStartTime;
        _logger.LogInformation("Scan completed successfully - Images: {ImageCount}, Files: {FileCount}, Duration: {DurationMs}ms, OutputPath: {OutputPath}",
            images.Count, files.Count, totalDuration.TotalMilliseconds, scanJobConfiguration.ExportPath);
        return ScanExecutionResult.Succeed(files);
    }

    private async Task<List<string>> SaveAsync(List<ProcessedImage> images, ScanJobConfiguration scanJobConfiguration)
    {
            var files = new List<string>();
            var name = scanJobConfiguration.FileName.Replace( "{datetime}",DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

        if (scanJobConfiguration.Format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {

            var path = Path.Combine(scanJobConfiguration.ExportPath, $"{name}.pdf");
            await new PdfExporter(_context!).Export(path, images);
            files.Add(path);
            _logger.LogDebug("Saved PDF: {Path}", path);
        }
        else if (scanJobConfiguration.Format.Equals("MultiPageTIFF", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < images.Count; i++)
            {
                var path = Path.Combine(scanJobConfiguration.ExportPath, $"{name}_{i + 1}.tiff");
                images[i].Save(path, ImageFileFormat.Tiff);
                files.Add(path);
            }
            _logger.LogDebug("Saved {Count} TIFF files", files.Count);
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

    private async Task<ScanDevice?> FindDeviceAsync(string deviceId)
    {
        var drivers = GetDrivers();

        foreach (var driver in drivers)
        {
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                //nothing
            }
            try
            {
                var devices = await _controller!.GetDeviceList(driver);
                var device = devices.FirstOrDefault(d => d.ID == deviceId);

                if (device != null)
                {
                    return device;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error searching for device i driver {Driver}", driver);
            }
        }
        return null;
    }
}
