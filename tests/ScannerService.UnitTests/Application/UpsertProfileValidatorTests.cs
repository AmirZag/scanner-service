using FluentValidation.Results;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class UpsertProfileValidatorTests
{
    [Fact]
    public void Validate_WithCanonicalDefaults_IsValid()
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("Invoices", true)]
    public void Validate_Name_IsRequired(string? name, bool expectedValid)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert(name!));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_Name_At100CharactersIsValid_At101IsNot()
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        Assert.True(validator.Validate(NewValidUpsert(new string('A', 100))).IsValid);
        Assert.False(validator.Validate(NewValidUpsert(new string('A', 101))).IsValid);
    }

    [Fact]
    public void Validate_AllowedValueSets_AreCaseInsensitive()
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        UpsertProfileDto dto = NewValidUpsert() with
        {
            PaperSource = "FEEDER",
            BitDepth = "grayscale",
            PageSize = "Letter",
            HorizontalAlign = "right",
            Scale = "1:4"
        };

        Assert.True(validator.Validate(dto).IsValid);
    }

    [Theory]
    [InlineData("PaperSource", "Auto")]
    [InlineData("BitDepth", "TrueColor")]
    [InlineData("PageSize", "A3")]
    [InlineData("HorizontalAlign", "Top")]
    [InlineData("Scale", "2:1")]
    public void Validate_DisallowedValue_InAnySetIsRejected(string propertyName, string value)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        UpsertProfileDto dto = propertyName switch
        {
            "PaperSource" => NewValidUpsert() with { PaperSource = value },
            "BitDepth" => NewValidUpsert() with { BitDepth = value },
            "PageSize" => NewValidUpsert() with { PageSize = value },
            "HorizontalAlign" => NewValidUpsert() with { HorizontalAlign = value },
            _ => NewValidUpsert() with { Scale = value }
        };

        ValidationResult result = validator.Validate(dto);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(1200, true)]
    [InlineData(1201, false)]
    public void Validate_Resolution_MustStayWithin50To1200(int resolution, bool expectedValid)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert() with { Resolution = resolution });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(-101, false)]
    [InlineData(-100, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_Brightness_MustStayWithinMinus100To100(int brightness, bool expectedValid)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert() with { Brightness = brightness });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(-101, false)]
    [InlineData(-100, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_Contrast_MustStayWithinMinus100To100(int contrast, bool expectedValid)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert() with { Contrast = contrast });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_ImageQuality_MustStayWithin1To100(int imageQuality, bool expectedValid)
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert() with { ImageQuality = imageQuality });

        Assert.Equal(expectedValid, result.IsValid);
    }

    // KNOWN BUG C-1c: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // UpsertProfileValidator has no DeviceId rule, so an empty device id is accepted and produces
    // a profile that can never scan (UpdateProfileValidator enforces the rule; this one does not).
    [Fact]
    public void Validate_EmptyDeviceId_CurrentBehavior_IsAccepted()
    {
        UpsertProfileValidator validator = new UpsertProfileValidator();

        ValidationResult result = validator.Validate(NewValidUpsert("Broken Device", string.Empty));

        Assert.True(result.IsValid);
    }

    private static UpsertProfileDto NewValidUpsert(string name = "Invoices", string? deviceId = null)
    {
        return new UpsertProfileDto(name, deviceId);
    }
}
