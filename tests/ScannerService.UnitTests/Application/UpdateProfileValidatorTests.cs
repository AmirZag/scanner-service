using FluentValidation.Results;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class UpdateProfileValidatorTests
{
    [Fact]
    public void Validate_AllNullPatch_IsValid()
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        ValidationResult result = validator.Validate(new UpdateProfileDto());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("Renamed", true)]
    public void Validate_Name_WhenProvidedMustBeNonEmpty(string? name, bool expectedValid)
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        ValidationResult result = validator.Validate(new UpdateProfileDto(name));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_Name_At100CharactersIsValid_At101IsNot()
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        Assert.True(validator.Validate(new UpdateProfileDto(new string('A', 100))).IsValid);
        Assert.False(validator.Validate(new UpdateProfileDto(new string('A', 101))).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("wia-flatbed", true)]
    public void Validate_DeviceId_WhenProvidedMustBeNonEmpty(string? deviceId, bool expectedValid)
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        ValidationResult result = validator.Validate(new UpdateProfileDto(DeviceId: deviceId));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_AllProvidedStringSets_AcceptCaseInsensitiveValues()
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        UpdateProfileDto dto = new UpdateProfileDto
        {
            PaperSource = "feeder",
            BitDepth = "GRAYSCALE",
            PageSize = "letter",
            HorizontalAlign = "Right",
            Scale = "1:8"
        };

        Assert.True(validator.Validate(dto).IsValid);
    }

    [Theory]
    [InlineData("PaperSource", "Auto")]
    [InlineData("BitDepth", "TrueColor")]
    [InlineData("PageSize", "A3")]
    [InlineData("HorizontalAlign", "Top")]
    [InlineData("Scale", "2:1")]
    public void Validate_ProvidedDisallowedValue_IsRejected(string propertyName, string value)
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        UpdateProfileDto dto = propertyName switch
        {
            "PaperSource" => new UpdateProfileDto { PaperSource = value },
            "BitDepth" => new UpdateProfileDto { BitDepth = value },
            "PageSize" => new UpdateProfileDto { PageSize = value },
            "HorizontalAlign" => new UpdateProfileDto { HorizontalAlign = value },
            _ => new UpdateProfileDto { Scale = value }
        };

        ValidationResult result = validator.Validate(dto);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(1200, true)]
    [InlineData(1201, false)]
    public void Validate_Resolution_WhenProvidedMustStayWithin50To1200(int? resolution, bool expectedValid)
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        ValidationResult result = validator.Validate(new UpdateProfileDto(Resolution: resolution));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_ProvidedBrightnessContrastAndImageQuality_MustRespectTheirRanges()
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        Assert.True(validator.Validate(new UpdateProfileDto(Brightness: -100, Contrast: 100, ImageQuality: 1)).IsValid);
        Assert.True(validator.Validate(new UpdateProfileDto(Brightness: 100, Contrast: -100, ImageQuality: 100)).IsValid);
        Assert.False(validator.Validate(new UpdateProfileDto(Brightness: -101)).IsValid);
        Assert.False(validator.Validate(new UpdateProfileDto(Contrast: 101)).IsValid);
        Assert.False(validator.Validate(new UpdateProfileDto(ImageQuality: 0)).IsValid);
        Assert.False(validator.Validate(new UpdateProfileDto(ImageQuality: 101)).IsValid);
    }

    [Fact]
    public void Validate_PartialPatchWithOnlySomeFields_AppliesRulesOnlyToProvidedFields()
    {
        UpdateProfileValidator validator = new UpdateProfileValidator();

        UpdateProfileDto dto = new UpdateProfileDto
        {
            Name = "Only Name",
            Resolution = 600,
            ImageQuality = 95
        };

        ValidationResult result = validator.Validate(dto);

        Assert.True(result.IsValid);
    }
}
