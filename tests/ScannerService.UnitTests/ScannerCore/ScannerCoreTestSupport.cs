using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.ImportExport;
using NAPS2.Scan;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Shared helpers for the ScannerCore area. Builds a production-shaped NAPS2 scanning context,
/// materializes real ProcessedImage instances by importing generated BMP files through the real
/// NAPS2 ImageImporter, and manages per-test temp directories. Everything runs headless: no
/// scanner drivers, worker processes, or hardware are touched.
/// </summary>
internal static class ScannerCoreTestSupport
{
    public static ScannerServiceType CreateScannerService(IScannerInitializer initializer)
    {
        return new ScannerServiceType(initializer, new ScannerTimeouts(), new List<EsclManualDevice>(), NullLogger<ScannerServiceType>.Instance);
    }

    public static ScanningContext CreateScanningContext()
    {
        return new ScanningContext(new GdiImageContext())
        {
            Logger = NullLogger.Instance
        };
    }

    public static ScanJobConfiguration CreateConfiguration(string exportDirectory, string format, string fileName)
    {
        return new ScanJobConfiguration
        {
            DeviceId = "test-device",
            Format = format,
            FileName = fileName,
            ExportPath = exportDirectory
        };
    }

    public static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scannercorerests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateSourceImageFile(string directory, string fileName, int width, int height)
    {
        string path = Path.Combine(directory, fileName);
        using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.Save(path, ImageFormat.Bmp);
        return path;
    }

    public static async Task<List<ProcessedImage>> ImportImageFileAsync(ScanningContext scanningContext, string sourcePath)
    {
        ImageImporter importer = new ImageImporter(scanningContext);
        List<ProcessedImage> images = new List<ProcessedImage>();
        await foreach (ProcessedImage image in importer.Import(sourcePath))
        {
            images.Add(image);
        }

        return images;
    }

    public static void DisposeImages(IEnumerable<ProcessedImage> images)
    {
        foreach (ProcessedImage image in images)
        {
            image.Dispose();
        }
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    public static void CleanSharedBinStateFiles()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DeleteFileIfExists(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"));
        DeleteFileIfExists(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json.tmp"));
        DeleteFileIfExists(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json.invalid"));
        DeleteFileIfExists(Path.Combine(AppContext.BaseDirectory, "scanner.db"));
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
