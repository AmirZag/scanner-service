using ScannerService.Application.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ContentTypesTests
{
    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".tif", "image/tiff")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".zip", "application/zip")]
    [InlineData(".PDF", "application/pdf")]
    [InlineData(".Jpg", "image/jpeg")]
    public void GetContentType_MapsEveryKnownExtension_CaseInsensitively(string extension, string expected)
    {
        string actual = ContentTypes.GetContentType(extension);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData("pdf")]
    [InlineData("")]
    public void GetContentType_FallsBackToOctetStream_ForUnknownOrMalformedExtensions(string extension)
    {
        string actual = ContentTypes.GetContentType(extension);

        Assert.Equal("application/octet-stream", actual);
    }

    [Theory]
    [InlineData(@"C:\Scans\scan_20261003_142530.pdf", "application/pdf")]
    [InlineData(@"C:\Scans\page1.jpg", "image/jpeg")]
    [InlineData(@"C:\Scans\archive.zip", "application/zip")]
    [InlineData(@"C:\Scans\scan_20261003_142530.PDF", "application/pdf")]
    public void GetContentTypeFromPath_ResolvesTheExtensionFromThePath(string filePath, string expected)
    {
        string actual = ContentTypes.GetContentTypeFromPath(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetContentTypeFromPath_WithoutAnExtension_FallsBackToOctetStream()
    {
        string actual = ContentTypes.GetContentTypeFromPath(@"C:\Scans\README");

        Assert.Equal("application/octet-stream", actual);
    }

    [Fact]
    public void GetContentTypeFromPath_UsesTheLastExtension_WhenTheNameContainsSeveralDots()
    {
        string actual = ContentTypes.GetContentTypeFromPath(@"C:\Scans\scan.tar.pdf");

        Assert.Equal("application/pdf", actual);
    }
}
