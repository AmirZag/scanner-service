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

    // KNOWN BUG A-4: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // GetBitmapViaTempFile materializes every page as an uncompressed scan_<guid>.bmp in %TEMP%
    // and only records it in an instance bag; nothing deletes them during SaveAsync, so they
    // persist until DisposeAsync (and are orphaned forever if the process dies first). The test
    // diffs the %TEMP% scan_*.bmp set around the call, asserts the per-page leftovers exist, and
    // then asserts the DisposeAsync sweep removes them again.
    [Fact]
    public async Task SaveAsync_MultiPageTiff_CurrentBehavior_TempBmpsPersistUntilDisposeAsync()
    {
        string tempRoot = Path.GetTempPath();
        string[] before = Directory.GetFiles(tempRoot, "scan_*.bmp");

        List<ProcessedImage> images = await ImportPagesAsync(2);
        Assert.Equal(2, images.Count);
        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.MultiPageTIFF, "doc"), _scanningContext);
        Assert.Single(files);

        string[] after = Directory.GetFiles(tempRoot, "scan_*.bmp");
        List<string> newLeftovers = after.Except(before, StringComparer.Ordinal).ToList();
        Assert.Equal(images.Count, newLeftovers.Count);
        foreach (string leftover in newLeftovers)
        {
            Assert.True(File.Exists(leftover), $"Expected temp BMP leftover to exist: {leftover}");
        }

        await _service.DisposeAsync();

        foreach (string leftover in newLeftovers)
        {
            Assert.False(File.Exists(leftover), $"Expected temp BMP leftover to be swept by DisposeAsync: {leftover}");
        }
    }

    [Fact]
    public async Task DisposeAsync_WhenTempBitmapFileIsLocked_SwallowsDeleteFailureAndCompletes()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);
        await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.MultiPageTIFF, "locked"), _scanningContext);

        FieldInfo bagField = typeof(ScannerServiceType).GetField("_tempBitmapFiles", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field _tempBitmapFiles not found on ScannerService.");
        List<string> bag = ((System.Collections.IEnumerable)bagField.GetValue(_service)!).Cast<string>().ToList();
        string lockedFile = Assert.Single(bag);
        Assert.True(File.Exists(lockedFile));

        using FileStream lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);
        await _service.DisposeAsync();

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
