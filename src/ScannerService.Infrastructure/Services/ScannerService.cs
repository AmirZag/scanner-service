using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.Scan;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Common;
using System.Collections.Concurrent;

namespace ScannerService.Infrastructure.Services;

public class ScannerService : IScannerQueries, IScannerService, IAsyncDisposable
{
    private readonly IScannerInitializer _initializer;
    private readonly ScannerTimeouts _timeouts;
    private readonly DriverHealthTracker _healthTracker;
    private readonly ILogger<ScannerService> _logger;

    /// <summary>
    /// Serializes scan jobs: a scanner is a physical serial device, so concurrent jobs are rejected
    /// (after a short queue wait) instead of being started against a busy device.
    /// </summary>
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly ConcurrentBag<string> _tempBitmapFiles = new();

    public ScannerService(IScannerInitializer initializer, ScannerTimeouts timeouts, ILogger<ScannerService> logger)
    {
        _initializer = initializer;
        _timeouts = timeouts;
        _healthTracker = new DriverHealthTracker(timeouts);
        _logger = logger;
    }

    public async Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving scanner list");
        await _initializer.InitializeAsync(cancellationToken);

        var drivers = ScannerDriverFactory.GetAvailableDrivers();
        var controller = ((IScannerInitializerContext)_initializer).Controller;

        // Each driver is queried with its own timeout budget; a hung driver is abandoned and skipped
        // so the other drivers still contribute their devices.
        var driverTasks = drivers
            .Select(async driver => (Driver: driver, Devices: await QueryDriverDevicesAsync(driver, controller, cancellationToken)))
            .ToList();

        var results = await Task.WhenAll(driverTasks);

        var scanners = new List<ScannerDto>();
        foreach (var (driver, devices) in results)
        {
            scanners.AddRange(devices.Select(d => new ScannerDto(d.ID, d.Name, driver.ToString())));
        }

        _logger.LogInformation("Found {TotalCount} total scanners across all drivers", scanners.Count);
        return scanners;
    }

    public async Task<Result<List<string>>> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration, CancellationToken cancellationToken = default)
    {
        var scanStartTime = DateTime.UtcNow;
        _logger.LogInformation("Starting scan operation - DeviceId: {DeviceId}, Format: {Format}, Resolution: {Resolution}",
            scanJobConfiguration.DeviceId, scanJobConfiguration.Format, scanJobConfiguration.Resolution);

        if (!await _scanGate.WaitAsync(_timeouts.ScanQueueTimeout, cancellationToken))
        {
            _logger.LogWarning("Scan rejected - another scan is in progress - DeviceId: {DeviceId}", scanJobConfiguration.DeviceId);
            return Result<List<string>>.Failure("Scanner is busy with another scan job. Please wait for it to finish and try again.");
        }

        try
        {
            return await ExecuteScanCoreAsync(scanJobConfiguration, scanStartTime, cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<Result<List<string>>> ExecuteScanCoreAsync(ScanJobConfiguration scanJobConfiguration, DateTime scanStartTime, CancellationToken cancellationToken)
    {
        await _initializer.InitializeAsync(cancellationToken);
        var context = ((IScannerInitializerContext)_initializer).Context;
        var controller = ((IScannerInitializerContext)_initializer).Controller;

        var device = await FindDeviceAsync(scanJobConfiguration.DeviceId, controller, cancellationToken);

        if (device == null)
        {
            _logger.LogError("Scanner device not found - DeviceId: {DeviceId}", scanJobConfiguration.DeviceId);
            return Result<List<string>>.Failure($"Scanner not found: {scanJobConfiguration.DeviceId}");
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

        // The scanner driver has no native cancellation (the token only bites at await points), so the
        // watchdog bounds the wait: a no-progress deadline re-armed after every page, while the overall
        // job cap is enforced by the WaitAsync race below.
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdogCts.CancelAfter(_timeouts.ScanNoProgressTimeout);

        var images = new List<ProcessedImage>();

        // The consumer owns the images until it completes successfully; on any failure, cancellation or
        // watchdog timeout it disposes whatever was captured so the caller never leaks ProcessedImage.
        var imagesOwnedByCaller = new bool[1];
        var consumeTask = Task.Run(async () =>
        {
            try
            {
                // watchdogCts is linked to cancellationToken (so client aborts bite at await points) and is
                // re-armed after every page; the WaitAsync race below bounds drivers that ignore the token.
#pragma warning disable S8949 // Passing the linked watchdog token is intentional; NAPS2 Scan has no token parameter
                await foreach (var image in controller.Scan(options).WithCancellation(watchdogCts.Token))
#pragma warning restore S8949
                {
                    images.Add(image);
                    watchdogCts.CancelAfter(_timeouts.ScanNoProgressTimeout);
                    _logger.LogDebug("Captured image {ImageNumber} at {Timestamp}", images.Count, DateTime.UtcNow);
                }
                imagesOwnedByCaller[0] = true;
                return true;
            }
            finally
            {
                if (!imagesOwnedByCaller[0])
                {
                    foreach (var img in images)
                    {
                        img.Dispose();
                    }
                }
            }
        }, watchdogCts.Token);

        List<string> files;
        try
        {
            bool completed = await consumeTask.WaitAsync(_timeouts.ScanOverallTimeout, cancellationToken);

            if (!completed || images.Count == 0)
            {
                var noImagesDuration = DateTime.UtcNow - scanStartTime;
                _logger.LogWarning("No images were scanned - Duration: {DurationMs}ms", noImagesDuration.TotalMilliseconds);
                return Result<List<string>>.Failure("No images were scanned. Please ensure the document is properly placed in the scanner and try again.");
            }
        }
        catch (TimeoutException ex)
        {
            await watchdogCts.CancelAsync();
            await ObserveAbortedConsumerAsync(consumeTask, images);

            var timeoutDuration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Scan timed out after {DurationMs}ms - the scanner may be offline or unresponsive - DeviceId: {DeviceId}",
                timeoutDuration.TotalMilliseconds, scanJobConfiguration.DeviceId);
            return Result<List<string>>.Failure(
                $"Scan timed out after {Math.Round(_timeouts.ScanOverallTimeout.TotalSeconds)} seconds. The scanner may be offline or unresponsive.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The requesting client is gone; let the consumer unwind and clean up on its own.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            var stallDuration = DateTime.UtcNow - scanStartTime;
            _logger.LogWarning(ex, "Scan aborted - no progress from the scanner within {NoProgressMs}ms - Duration: {DurationMs}ms - DeviceId: {DeviceId}",
                _timeouts.ScanNoProgressTimeoutMs, stallDuration.TotalMilliseconds, scanJobConfiguration.DeviceId);
            return Result<List<string>>.Failure("Scan aborted because the scanner made no progress. The scanner may be offline or unresponsive.");
        }
        catch (Exception ex)
        {
            var failureDuration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Error during scan operation - Duration: {DurationMs}ms", failureDuration.TotalMilliseconds);
            return Result<List<string>>.Failure($"Scan operation failed: {ex.Message}");
        }

        _logger.LogInformation("Scanned {ImageCount} images, saving as {Format}", images.Count, scanJobConfiguration.Format);

        try
        {
            files = await SaveAsync(images, scanJobConfiguration, context);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - scanStartTime;
            _logger.LogError(ex, "Failed to save scanned images - Duration: {DurationMs}ms", duration.TotalMilliseconds);
            return Result<List<string>>.Failure($"Failed to save scanned images: {ex.Message}");
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
        return Result<List<string>>.Success(files);
    }

    /// <summary>
    /// Waits briefly for a scan consumer aborted by the watchdog so it can finish disposing partial images.
    /// If it succeeded in that window (deadline hit exactly as the last page arrived) the still-live images
    /// are disposed here; a faulted consumer is observed to avoid an unobserved task exception.
    /// </summary>
    private async Task ObserveAbortedConsumerAsync(Task<bool> consumeTask, List<ProcessedImage> images)
    {
        try
        {
            await consumeTask.WaitAsync(TimeSpan.FromSeconds(2));
            if (await consumeTask)
            {
                foreach (var img in images)
                {
                    img.Dispose();
                }
            }
        }
        catch (TimeoutException ex)
        {
            _logger.LogDebug(ex, "Scan consumer is still unwinding after watchdog cancellation; it will clean up its own images");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Scan consumer stopped after watchdog cancellation");
        }
    }

    /// <summary>
    /// Queries a single driver for its devices under a strict timeout budget.
    /// Drivers known to be unresponsive (cooling down) or unusable are skipped; on timeout the partial
    /// results collected so far are returned and the driver enters a cool-down instead of being retried
    /// immediately on the next request.
    /// </summary>
    private async Task<List<ScanDevice>> QueryDriverDevicesAsync(Driver driver, ScanController controller, CancellationToken cancellationToken)
    {
        if (ScannerDriverFactory.ShouldSkipDriver(driver, _initializer.TwainWorkerFailed))
        {
            _logger.LogDebug("Skipping driver {Driver} due to TWAIN worker initialization failure", driver);
            return [];
        }

        if (_healthTracker.IsCoolingDown(driver))
        {
            _logger.LogDebug("Skipping driver {Driver} until {RetryAt} - cooling down after unresponsiveness",
                driver, _healthTracker.CoolDownEnd(driver));
            return [];
        }

        TimeSpan budget = driver == Driver.Escl ? _timeouts.EsclDeviceSearchBudget : _timeouts.DriverTimeout;

        // Native enumeration (WIA/TWAIN) cannot be interrupted; the token bounds only drivers that honor
        // it (ESCL). The collector owns its queue so an abandoned task can never mutate a list that was
        // already returned to a caller.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            linkedCts.CancelAfter(budget);

            var devices = new ConcurrentQueue<ScanDevice>();
            var collectTask = Task.Run(async () =>
            {
                try
                {
                    var options = new ScanOptions { Driver = driver };
                    options.EsclOptions ??= new EsclOptions();
                    options.EsclOptions.SearchTimeout = _timeouts.EsclSearchTimeoutMs;
                    await foreach (var device in controller.GetDevices(options, linkedCts.Token))
                    {
                        devices.Enqueue(device);
                    }
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Device enumeration failed for driver {Driver}", driver);
                    return false;
                }
            }, linkedCts.Token);

            try
            {
                bool completed = await collectTask.WaitAsync(budget, cancellationToken);
                if (completed)
                {
                    _healthTracker.RecordSuccess(driver);
                }
                return devices.ToList();
            }
            catch (TimeoutException ex)
            {
                _healthTracker.RecordTimeout(driver);
                var snapshot = devices.ToList();
                _logger.LogWarning(ex,
                    "Device enumeration for driver {Driver} exceeded {BudgetMs}ms; returning {DeviceCount} partial device(s) and cooling down until {RetryAt}",
                    driver, budget.TotalMilliseconds, snapshot.Count, _healthTracker.CoolDownEnd(driver));
                return snapshot;
            }
        }
        finally
        {
            // Cancel before disposing so drivers honoring the token (ESCL) unwind instead of leaking a
            // pending delay on a disposed source.
            await linkedCts.CancelAsync();
            linkedCts.Dispose();
        }
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

        try
        {
            _scanGate.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose scan gate");
        }

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

    private async Task<ScanDevice?> FindDeviceAsync(string deviceId, ScanController controller, CancellationToken cancellationToken)
    {
        var drivers = ScannerDriverFactory.GetAvailableDrivers();

        // Bounded by the per-driver budgets in QueryDriverDevicesAsync; the drivers are queried in
        // parallel and the first matching device wins.
        var driverTasks = drivers
            .Select(async driver => await QueryDriverDevicesAsync(driver, controller, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(driverTasks);

        foreach (var devices in results)
        {
            var device = devices.FirstOrDefault(d => d.ID == deviceId);
            if (device != null)
            {
                return device;
            }
        }

        return null;
    }
}
