using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ProfileUpdateExtensionsTests
{
    [Fact]
    public void ToUpdateOptions_MapsEveryDtoFieldIntoTheOptions()
    {
        UpdateProfileDto dto = new UpdateProfileDto(
            "Renamed",
            "escl-http-9100",
            ScannerConstants.PaperSource.Feeder,
            ScannerConstants.BitDepth.Grayscale,
            600,
            ScannerConstants.PageSize.Legal,
            ScannerConstants.HorizontalAlign.Left,
            ScannerConstants.Scale.HalfSize,
            10,
            -10,
            90);

        ProfileUpdateOptions options = dto.ToUpdateOptions();

        Assert.Equal("Renamed", options.Name);
        Assert.Equal("escl-http-9100", options.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Feeder, options.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Grayscale, options.BitDepth);
        Assert.Equal(600, options.Resolution);
        Assert.Equal(ScannerConstants.PageSize.Legal, options.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Left, options.HorizontalAlign);
        Assert.Equal(ScannerConstants.Scale.HalfSize, options.Scale);
        Assert.Equal(10, options.Brightness);
        Assert.Equal(-10, options.Contrast);
        Assert.Equal(90, options.ImageQuality);
    }

    [Fact]
    public void ToUpdateOptions_WithAnAllNullPatch_PreservesNullsAsAbsenceOfChange()
    {
        UpdateProfileDto dto = new UpdateProfileDto();

        ProfileUpdateOptions options = dto.ToUpdateOptions();

        Assert.Null(options.Name);
        Assert.Null(options.DeviceId);
        Assert.Null(options.PaperSource);
        Assert.Null(options.BitDepth);
        Assert.Null(options.Resolution);
        Assert.Null(options.PageSize);
        Assert.Null(options.HorizontalAlign);
        Assert.Null(options.Scale);
        Assert.Null(options.Brightness);
        Assert.Null(options.Contrast);
        Assert.Null(options.ImageQuality);
    }
}
