using Microsoft.Extensions.Logging;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.Scan;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ScannerService.Infrastructure.Services;

public class ScannerDeviceProvider : IScannerDevice, IScannerDeviceService, IAsyncDisposable
{
    private ScanningContext? _context;
    private ScanController? _controller;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;
    private bool _twainWorkerFailed;
    private readonly ILogger<ScannerDeviceProvider> _logger;

    public ScannerDeviceProvider(ILogger<ScannerDeviceProvider> logger)
    {
        _logger = logger;
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        await _lock.WaitAsync();
        try
        {
            if (_initialized) return;

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
                    _logger.LogInformation("TWAIN worker initialized successfully");
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

    public async Task<List<ScannerDto>> GetScannersAsync()
    {
        _logger.LogDebug("Retrieving scanner list");
        await InitializeAsync();

        var scanners = new List<ScannerDto>();
        var drivers = GetDrivers();

        foreach (var driver in drivers)
        {
            // Skip TWAIN if worker failed
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                _logger.LogDebug("Skipping TWAIN driver due to worker initialization failure");
                continue;
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

    public async Task<List<string>> DeviceScanAsync(ScanJobConfiguration request)
    {
        _logger.LogInformation("Starting scan operation with device {DeviceId}", request.DeviceId);
        await InitializeAsync();

        var device = await FindDeviceAsync(request.DeviceId);
        if (device == null)
        {
            _logger.LogError("Scanner device not found: {DeviceId}", request.DeviceId);
            throw new InvalidOperationException($"Scanner not found: {request.DeviceId}");
        }

        var options = new NAPS2.Scan.ScanOptions
        {
            Device = device,
            Dpi = request.Resolution,
            BitDepth = request.BitDepth switch
            {
                "BlackAndWhite" => BitDepth.BlackAndWhite,
                "Grayscale" => BitDepth.Grayscale,
                _ => BitDepth.Color
            },
            PaperSource = request.PaperSource.Equals("Feeder", StringComparison.OrdinalIgnoreCase)
                ? NAPS2.Scan.PaperSource.Feeder
                : NAPS2.Scan.PaperSource.Flatbed
        };

        _logger.LogDebug("Scan options: Resolution={Dpi}, BitDepth={BitDepth}, Source={Source}",
            options.Dpi, request.BitDepth, request.PaperSource);

        var images = new List<ProcessedImage>();
        await foreach (var image in _controller!.Scan(options))
        {
            images.Add(image);
            _logger.LogDebug("Captured image {Count}", images.Count);
        }

        if (images.Count == 0)
        {
            _logger.LogWarning("No images were scanned");
            throw new InvalidOperationException("No images scanned");
        }

        _logger.LogInformation("Scanned {Count} images, saving to {Format}", images.Count, request.OutputFormat);
        var files = await SaveAsync(images, request);

        foreach (var img in images) img.Dispose();

        _logger.LogInformation("Scan complete, saved {Count} files", files.Count);
        return files;
    }

    private async Task<ScanDevice?> FindDeviceAsync(string deviceId)
    {
        var drivers = GetDrivers();
        foreach (var driver in drivers)
        {
            // Skip TWAIN if worker failed
            if (driver == Driver.Twain && _twainWorkerFailed)
            {
                continue;
            }

            try
            {
                var devices = await _controller!.GetDeviceList(driver);
                var device = devices.FirstOrDefault(d => d.ID == deviceId);
                if (device != null) return device;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error searching for device in driver {Driver}", driver);
            }
        }
        return null;
    }

    private async Task<List<string>> SaveAsync(List<ProcessedImage> images, ScanJobConfiguration req)
    {
        var files = new List<string>();
        var name = req.FileName.Replace("{datetime}", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        if (req.OutputFormat.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(req.OutputPath, $"{name}.pdf");
            await new PdfExporter(_context!).Export(path, images);
            files.Add(path);
            _logger.LogDebug("Saved PDF: {Path}", path);
        }
        else if (req.OutputFormat.Equals("MultiPageTIFF", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < images.Count; i++)
            {
                var path = Path.Combine(req.OutputPath, $"{name}_{i + 1}.tiff");
                images[i].Save(path, ImageFileFormat.Tiff);
                files.Add(path);
            }
            _logger.LogDebug("Saved {Count} TIFF files", files.Count);
        }
        else
        {
            var format = req.OutputFormat.ToLowerInvariant() switch
            {
                "png" => ImageFileFormat.Png,
                "tiff" => ImageFileFormat.Tiff,
                _ => ImageFileFormat.Jpeg
            };

            for (int i = 0; i < images.Count; i++)
            {
                var ext = req.OutputFormat.ToLowerInvariant();
                var path = Path.Combine(req.OutputPath, $"{name}_{i + 1}.{ext}");
                images[i].Save(path, format);
                files.Add(path);
            }
            _logger.LogDebug("Saved {Count} {Format} files", files.Count, req.OutputFormat);
        }

        return files;
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
}