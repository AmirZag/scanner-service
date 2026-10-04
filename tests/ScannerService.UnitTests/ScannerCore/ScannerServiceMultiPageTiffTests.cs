using System.Drawing.Imaging;
using System.Reflection;
using NAPS2.Images;
using NAPS2.Scan;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for the multi-page TIFF branch of ScannerServiceType.SaveAsync (the GDI+ SaveAdd chain, via
/// per-page temp BMPs) and the temp-file lifecycle around it (audit finding A-4). The class joins
/// the shared bin-state collection because it is the only ScannerCore producer of scan_*.bmp
/// files in the shared %TEMP% folder and must not race the before/after diff.
/// </summary>
[Collection("BinState")]
public sealed class ScannerServiceMultiPageTiffTests : IAsyncLifetime
{
    private readonly List<ProcessedImage> _createdImages = new List<ProcessedImage>();
    private ScannerServiceType _service = null!;
    private ScanningContext _scanningContext = null!;
    private string _exportDirectory = string.Empty;

    public Task InitializeAsync()
    {
        ScannerCoreTestSupport.CleanSharedBinStateFiles();
        FakeDisposableScannerInitializer initializer = new FakeDisposableScannerInitializer();
        _service = ScannerCoreTestSupport.CreateScannerService(initializer);
        _scanningContext = ScannerCoreTestSupport.CreateScanningContext();
        _exportDirectory = ScannerCoreTestSupport.CreateTempDirectory();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScannerCoreTestSupport.DisposeImages(_createdImages);
        _scanningContext.Dispose();
        await _service.DisposeAsync();
        ScannerCoreTestSupport.DeleteDirectoryIfExists(_exportDirectory);
        ScannerCoreTestSupport.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task SaveAsync_MultiPageTiff_WritesSingleTiffContainingAllPages()
    {
        List<ProcessedImage> images = await ImportPagesAsync(2);
        Assert.Equal(2, images.Count);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.MultiPageTIFF, "doc"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Equal(Path.Combine(_exportDirectory, "doc.tiff"), savedFile);
        Assert.True(File.Exists(savedFile));
        Assert.True(new FileInfo(savedFile).Length > 0);
        using (System.Drawing.Image savedTiff = System.Drawing.Image.FromFile(savedFile))
        {
            FrameDimension pageDimension = new FrameDimension(savedTiff.FrameDimensionsList[0]);
            int pageCount = savedTiff.GetFrameCount(pageDimension);
            Assert.Equal(2, pageCount);
        }
    }

    // FIXED (Phase 2 Batch 1, audit A-4): each page's uncompressed temp BMP is deleted as soon as
    // its bitmap is disposed (the first page's when the multi-frame document completes), so a
    // completed multi-page TIFF leaves no scan_*.bmp leftovers in %TEMP% - and a crash mid-save
    // can only orphan the pages still in flight, not every page before them.
    [Fact]
    public async Task SaveAsync_MultiPageTiff_DeletesTempBmpsWhenDone()
    {
        string tempRoot = Path.GetTempPath();
        string[] before = Directory.GetFiles(tempRoot, "scan_*.bmp");

        List<ProcessedImage> images = await ImportPagesAsync(2);
        Assert.Equal(2, images.Count);
        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.MultiPageTIFF, "doc"), _scanningContext);
        Assert.Single(files);

        string[] after = Directory.GetFiles(tempRoot, "scan_*.bmp");
        Assert.Empty(after.Except(before, StringComparer.Ordinal));

        FieldInfo bagField = typeof(ScannerServiceType).GetField("_tempBitmapFiles", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field _tempBitmapFiles not found on ScannerService.");
        List<string> bag = ((System.Collections.IDictionary)bagField.GetValue(_service)!).Keys.Cast<string>().ToList();
        Assert.Empty(bag);
    }

    [Fact]
    public async Task DisposeAsync_WhenBackstopBagHoldsLockedTempBitmap_SwallowsDeleteFailureAndCompletes()
    {
        // After the A-4 fix a normal save empties the bag itself; the DisposeAsync sweep is the
        // backstop for entries that outlive an exception path. Simulating one: a real locked file
        // placed in the bag makes the sweep's delete fail, which must be swallowed so shutdown
        // completes.
        string lockedFile = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.bmp");
        File.WriteAllText(lockedFile, "locked");
        using FileStream lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        FieldInfo bagField = typeof(ScannerServiceType).GetField("_tempBitmapFiles", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field _tempBitmapFiles not found on ScannerService.");
        System.Collections.IDictionary bag = (System.Collections.IDictionary)bagField.GetValue(_service)!;
        bag[lockedFile] = (byte)0;

        Exception? disposalFailure = await Record.ExceptionAsync(async () => await _service.DisposeAsync());

        // The sweep's delete failure on the locked file is swallowed, the entry is still removed
        // from the bag, and shutdown completes.
        Assert.Null(disposalFailure);
        Assert.Empty(bag.Keys.Cast<string>());

        lockStream.Dispose();
        File.Delete(lockedFile);
    }

    private async Task<List<ProcessedImage>> ImportPagesAsync(int pageCount)
    {
        List<ProcessedImage> images = new List<ProcessedImage>();
        for (int i = 0; i < pageCount; i++)
        {
            string sourcePath = ScannerCoreTestSupport.CreateSourceImageFile(_exportDirectory, $"source_page_{i}.bmp", 64, 48);
            List<ProcessedImage> imported = await ScannerCoreTestSupport.ImportImageFileAsync(_scanningContext, sourcePath);
            Assert.Single(imported);
            images.AddRange(imported);
            _createdImages.AddRange(imported);
        }

        return images;
    }

    private ScanJobConfiguration CreateConfig(string format, string fileName)
    {
        return ScannerCoreTestSupport.CreateConfiguration(_exportDirectory, format, fileName);
    }
}
