using NAPS2.Images;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for ScannerServiceType.ParseImageFileFormat, the pure format-string-to-NAPS2-enum mapping
/// applied to the stored Profile Format value in the per-image save path. Recognized formats are
/// case-insensitive ("png", "PNG", "Png"); everything else falls back to Jpeg.
/// </summary>
public sealed class ParseImageFileFormatTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("PNG")]
    [InlineData("Png")]
    public void ParseImageFileFormat_PngCasingVariants_ReturnsPng(string format)
    {
        ImageFileFormat parsed = ScannerServiceType.ParseImageFileFormat(format);

        Assert.Equal(ImageFileFormat.Png, parsed);
    }

    [Theory]
    [InlineData("tiff")]
    [InlineData("TIFF")]
    public void ParseImageFileFormat_TiffCasingVariants_ReturnsTiff(string format)
    {
        ImageFileFormat parsed = ScannerServiceType.ParseImageFileFormat(format);

        Assert.Equal(ImageFileFormat.Tiff, parsed);
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("JPEG")]
    public void ParseImageFileFormat_JpegCasingVariants_ReturnsJpeg(string format)
    {
        ImageFileFormat parsed = ScannerServiceType.ParseImageFileFormat(format);

        Assert.Equal(ImageFileFormat.Jpeg, parsed);
    }

    [Fact]
    public void ParseImageFileFormat_UnknownFormat_FallsBackToJpeg()
    {
        ImageFileFormat parsed = ScannerServiceType.ParseImageFileFormat("webp");

        Assert.Equal(ImageFileFormat.Jpeg, parsed);
    }
}
