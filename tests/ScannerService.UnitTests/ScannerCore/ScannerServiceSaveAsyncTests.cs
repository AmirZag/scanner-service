using System.Drawing.Imaging;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NAPS2.Images;
using NAPS2.Scan;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Integration-style tests for ScannerServiceType.SaveAsync, the file-output pipeline. Pages are real
/// ProcessedImage instances imported through the real NAPS2 ImageImporter from generated BMP
/// files, and outputs are verified on disk (existence, non-empty, magic bytes / decodability), so
/// the whole GDI+/pdfium export chain is exercised headless without any scanner hardware.
/// The class joins the shared bin-state collection because SaveAsync writes to the shared %TEMP%
/// folder and the tests must not overlap other file-creating areas.
/// </summary>
[Collection("BinState")]
public sealed class ScannerServiceSaveAsyncTests : IAsyncLifetime
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
    public async Task SaveAsync_PdfFormat_CreatesNonEmptyPdfFile()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.PDF, "report"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Equal(Path.Combine(_exportDirectory, "report.pdf"), savedFile);
        Assert.True(File.Exists(savedFile));
        Assert.True(new FileInfo(savedFile).Length > 0);
        byte[] pdfBytes = File.ReadAllBytes(savedFile);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdfBytes, 0, 4));
    }

    [Fact]
    public async Task SaveAsync_PdfFormatLowercase_IsCaseInsensitive()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);

        List<string> files = await _service.SaveAsync(images, CreateConfig("pdf", "report"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Equal(Path.Combine(_exportDirectory, "report.pdf"), savedFile);
        Assert.True(File.Exists(savedFile));
        Assert.True(new FileInfo(savedFile).Length > 0);
        byte[] pdfBytes = File.ReadAllBytes(savedFile);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdfBytes, 0, 4));
    }

    [Fact]
    public async Task SaveAsync_DateTimeTokenInFileName_IsReplacedWithTimestamp()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.PDF, "doc_{datetime}"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Matches(@"^doc_\d{8}_\d{6}\.pdf$", Path.GetFileName(savedFile));
        Assert.True(File.Exists(savedFile));
        Assert.True(new FileInfo(savedFile).Length > 0);
    }

    [Fact]
    public async Task SaveAsync_JpegFormat_WritesNumberedFilesWithJpegContent()
    {
        List<ProcessedImage> images = await ImportPagesAsync(2);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.JPEG, "doc"), _scanningContext);

        Assert.Equal(2, files.Count);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_1.jpeg"), files[0]);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_2.jpeg"), files[1]);
        Assert.True(File.Exists(files[0]));
        Assert.True(File.Exists(files[1]));
        AssertJpegContent(files[0]);
        AssertJpegContent(files[1]);
    }

    [Fact]
    public async Task SaveAsync_PngFormat_WritesNumberedFilesWithPngContent()
    {
        List<ProcessedImage> images = await ImportPagesAsync(2);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.PNG, "doc"), _scanningContext);

        Assert.Equal(2, files.Count);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_1.png"), files[0]);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_2.png"), files[1]);
        Assert.True(File.Exists(files[0]));
        Assert.True(File.Exists(files[1]));
        AssertPngContent(files[0]);
        AssertPngContent(files[1]);
    }

    [Fact]
    public async Task SaveAsync_TiffFormat_WritesOneFilePerPage()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);

        List<string> files = await _service.SaveAsync(images, CreateConfig(ScannerConstants.ExportFormat.TIFF, "doc"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_1.tiff"), savedFile);
        Assert.True(File.Exists(savedFile));
        Assert.True(new FileInfo(savedFile).Length > 0);
        using (System.Drawing.Image savedTiff = System.Drawing.Image.FromFile(savedFile))
        {
            Assert.Equal(System.Drawing.Imaging.ImageFormat.Tiff, savedTiff.RawFormat);
        }
    }

    // ParseImageFileFormat maps unknown format strings to Jpeg, while the output extension is the
    // raw config.Format string. Pin both halves: the file keeps the configured extension but
    // carries JPEG content (SOI marker), so extension-based dispatch downstream would misread it.
    [Fact]
    public async Task SaveAsync_UnknownFormatString_CurrentBehavior_EncodesJpegWithRawExtension()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);

        List<string> files = await _service.SaveAsync(images, CreateConfig("webp", "doc"), _scanningContext);

        string savedFile = Assert.Single(files);
        Assert.Equal(Path.Combine(_exportDirectory, "doc_1.webp"), savedFile);
        Assert.True(File.Exists(savedFile));
        AssertJpegContent(savedFile);
    }

    // FIXED (Phase 2 Batch 2, audit AR-3): output paths resolve collision-free, so two scans of
    // the same profile inside one second no longer overwrite the first scan's documents - the
    // second export lands on a "_2" sibling. Seeding the colliding file directly (instead of
    // relying on second-boundary timing) keeps the test deterministic.
    [Fact]
    public async Task SaveAsync_CollidingOutputName_GetsUniquePathAndKeepsFirstFile()
    {
        List<ProcessedImage> images = await ImportPagesAsync(1);
        ScanJobConfiguration config = CreateConfig(ScannerConstants.ExportFormat.PDF, "doc_fixed");

        string existingPath = Path.Combine(_exportDirectory, "doc_fixed.pdf");
        File.WriteAllText(existingPath, "first-scan-document");

        List<string> files = await _service.SaveAsync(images, config, _scanningContext);

        string secondPath = Assert.Single(files);
        Assert.NotEqual(existingPath, secondPath);
        Assert.Matches(@"^doc_fixed_2\.pdf$", Path.GetFileName(secondPath));
        Assert.True(File.Exists(secondPath));
        Assert.True(new FileInfo(secondPath).Length > 0);
        Assert.Equal("first-scan-document", await File.ReadAllTextAsync(existingPath));
    }

    [Fact]
    public async Task SaveAsync_CollidingImagePageNames_GetUniquePathsForEveryPage()
    {
        List<ProcessedImage> images = await ImportPagesAsync(2);
        ScanJobConfiguration config = CreateConfig(ScannerConstants.ExportFormat.JPEG, "doc_fixed");

        // Pre-occupy every default page name of the first attempt; each page must shift onto its
        // own collision-free path rather than overwrite. (The JPEG export writes ".jpeg" — the
        // lowercased format constant.)
        foreach (string occupied in new[] { "doc_fixed_1.jpeg", "doc_fixed_2.jpeg" })
        {
            File.WriteAllText(Path.Combine(_exportDirectory, occupied), "first-scan-page");
        }

        List<string> files = await _service.SaveAsync(images, config, _scanningContext);

        Assert.Equal(2, files.Count);
        Assert.All(files, file =>
        {
            Assert.True(File.Exists(file));
            Assert.True(new FileInfo(file).Length > 0);
            Assert.NotEqual("first-scan-page", File.ReadAllText(file));
        });
        Assert.Equal(
            new[] { "doc_fixed_1_2.jpeg", "doc_fixed_2_2.jpeg" },
            files.Select(file => Path.GetFileName(file)).ToArray());
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

    private static void AssertJpegContent(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        Assert.True(content.Length > 2);
        Assert.Equal(0xFF, content[0]);
        Assert.Equal(0xD8, content[1]);
    }

    private static void AssertPngContent(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        Assert.True(content.Length > 4);
        Assert.Equal(0x89, content[0]);
        Assert.Equal(0x50, content[1]);
        Assert.Equal(0x4E, content[2]);
        Assert.Equal(0x47, content[3]);
    }
}
