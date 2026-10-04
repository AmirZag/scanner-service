using FluentValidation.Results;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ScanRequestValidatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(42, true)]
    public void Validate_ProfileId_MustBeGreaterThanZero(int profileId, bool expectedValid)
    {
        ScanRequestValidator validator = new ScanRequestValidator();

        ValidationResult result = validator.Validate(new ScanRequestDto(profileId));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_WithDefaultOptionalFields_IsValid()
    {
        ScanRequestValidator validator = new ScanRequestValidator();

        ValidationResult result = validator.Validate(new ScanRequestDto(1));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // FIXED (Phase 2 Batch 4, audit C-1d): the Format whitelist is enforced, so an arbitrary
    // string can no longer become the output file extension. Null still passes (keep the export
    // setting's configured format).
    [Fact]
    public void Validate_ArbitraryFormat_IsRejected()
    {
        ScanRequestValidator validator = new ScanRequestValidator();

        ValidationResult result = validator.Validate(new ScanRequestDto(1, @"C:\evil", "evil"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.ErrorMessage == "Format must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF");

        Assert.True(validator.Validate(new ScanRequestDto(1, @"C:\evil", null)).IsValid);
    }
}
