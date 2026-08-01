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
using ScannerService.Domain.Common;
using System.Collections.Concurrent;

namespace ScannerService.Infrastructure.Services;

public class ScannerService : IScannerQueries, IScannerService, IAsyncDisposable
{
    private readonly IScannerInitializer _initializer;
    private readonly ILogger<ScannerService> _logger;
    private readonly ConcurrentBag<string> _tempBitmapFiles = new();

    public ScannerService(IScannerInitializer initializer, ILogger<ScannerService> _logger)
    {
        _initializer = initializer;
        this._logger = _logger;
    }

    public async Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving scanner list");
        await _initializer.InitializeAsync(cancellationToken);

        var drivers = ScannerDriverFactory.GetAvailableDrivers();
        var controller = ((IScannerInitializerContext)_initializer).Controller;

        // Query drivers in parallel for better performance
        var driverTasks = drivers.Select(async driver =>
        {
            if (ScannerDriverFactory.ShouldSkipDriver(driver, _initializer.TwainWorkerFailed))
            {
                _logger.LogDebug("Skipping TWAIN driver due to worker initialization failure");
                return (Scanners: new List<ScannerDto>(), Driver: driver);
            }

            try
            {
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

    public async Task<ScanExecutionResult> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration, CancellationToken cancellationToken = default)
    {
        var scanStartTime = DateTime.UtcNow;
        _logger.LogInformation("Starting scan operation - DeviceId: {DeviceId}, Format: {Format}, Resolution: {Resolution}",
            scanJobConfiguration.DeviceId, scanJobConfiguration.Format, scanJobConfiguration.Resolution);

        await _initializer.InitializeAsync(cancellationToken);
        var context = ((IScannerInitializerContext)_initializer).Context;
        var controller = ((IScannerInitializerContext)_initializer).Controller;

        var device = await FindDeviceAsync(scanJobConfiguration.DeviceId, controller, cancellationToken);

        if (device == null)
        {
            _logger.LogError("Scanner device not found - DeviceId: {DeviceId}", scanJobConfiguration.DeviceId);
            return ScanExecutionResult.Fail($"Scanner not found: {scanJobConfiguration.DeviceId}");
        }

        var options = new NAPS2.Scan.ScanOptions
        {
            Device = device,
            Dpi = scanJobConfiguration.Resolution,
            BitDepth = ParseBitDepth(scanJobConfiguration.BitDepth),
            PaperSource = ParsePaperSource(scanJobConfiguration.PaperSource)
        };

        _logger.LogDebug("Scan options configured - Dpi: {Dpi}, BitDepth: {BitDepth}, PaperSource: {PaperSource}",
            options.Dpi, options.BitDepth, options.PaperSource);

        var images = new List<ProcessedImage>();

        try
        {
#pragma warning disable CA2016, S8949 // NAPS2 Scan method doesn't support cancellation tokens
            await foreach (var image in controller.Scan(options).WithCancellation(CancellationToken.None))
#pragma warning restore CA2016, S8949
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
            files = await SaveAsync(images, scanJobConfiguration, context);
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

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing scanner provider");

        // Clean up temporary bitmap files
        foreach (var tempFile in _tempBitmapFiles)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                    _logger.LogDebug("Cleaned up temp bitmap file: {TempFile}", tempFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp bitmap file: {TempFile}", tempFile);
            }
        }
        _tempBitmapFiles.Clear();

        if (_initializer is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        await Task.CompletedTask;
    }

    private async Task<List<string>> SaveAsync(List<ProcessedImage> images, ScanJobConfiguration config, ScanningContext context)
    {
        var files = new List<string>();
        var name = config.FileName.Replace("{datetime}", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

        if (config.Format.Equals(ScannerConstants.ExportFormat.PDF, StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(config.ExportPath, $"{name}.pdf");
            await new PdfExporter(context).Export(path, images);
            files.Add(path);
            _logger.LogDebug("Saved PDF: {Path}", path);
        }
        else if (config.Format.Equals(ScannerConstants.ExportFormat.MultiPageTIFF, StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(config.ExportPath, $"{name}.tiff");
            await SaveMultiPageTiffAsync(images, path);
            files.Add(path);
            _logger.LogDebug("Saved multi-page TIFF: {Path}", path);
        }
        else
        {
            var format = ParseImageFileFormat(config.Format);

            for (int i = 0; i < images.Count; i++)
            {
                var ext = config.Format.ToLowerInvariant();
                var path = Path.Combine(config.ExportPath, $"{name}_{i + 1}.{ext}");
                images[i].Save(path, format);
                files.Add(path);
            }
            _logger.LogDebug("Saved {Count} {Format} files", files.Count, config.Format);
        }

        return files;
    }

    private static BitDepth ParseBitDepth(string bitDepth)
    {
        return bitDepth switch
        {
            ScannerConstants.BitDepth.BlackAndWhite => BitDepth.BlackAndWhite,
            ScannerConstants.BitDepth.Grayscale => BitDepth.Grayscale,
            _ => BitDepth.Color
        };
    }

    private static NAPS2.Scan.PaperSource ParsePaperSource(string paperSource)
    {
        return paperSource.Equals(ScannerConstants.PaperSource.Feeder, StringComparison.OrdinalIgnoreCase)
            ? NAPS2.Scan.PaperSource.Feeder
            : NAPS2.Scan.PaperSource.Flatbed;
    }

    private static ImageFileFormat ParseImageFileFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "png" => ImageFileFormat.Png,
            "tiff" => ImageFileFormat.Tiff,
            _ => ImageFileFormat.Jpeg
        };
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

    private Bitmap GetBitmapFromImage(ProcessedImage image)
    {
        return GetBitmapViaTempFile(image);
    }

    private Bitmap GetBitmapViaTempFile(ProcessedImage image)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.bmp");
        image.Save(tempPath, ImageFileFormat.Bmp);
        _tempBitmapFiles.Add(tempPath);
        return new Bitmap(tempPath);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Code", "IDE0060:Remove unused parameter", Justification = "CancellationToken kept for interface consistency")]
    private async Task<ScanDevice?> FindDeviceAsync(string deviceId, ScanController controller, CancellationToken cancellationToken)
    {
        var drivers = ScannerDriverFactory.GetAvailableDrivers();

        // Query drivers in parallel, but return as soon as we find the device
        var driverTasks = drivers.Select(async driver =>
        {
            if (ScannerDriverFactory.ShouldSkipDriver(driver, _initializer.TwainWorkerFailed))
            {
                return (Device: (ScanDevice?)null, Driver: driver);
            }

            try
            {
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
