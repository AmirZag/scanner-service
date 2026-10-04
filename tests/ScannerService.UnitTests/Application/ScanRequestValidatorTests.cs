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

    // KNOWN BUG C-1d: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // ScanRequestValidator checks only ProfileId, so an arbitrary Format string is accepted and
    // later becomes the output file extension, bypassing the format whitelist the settings path
    // enforces (ExportSettingValidator.AllowedFormats).
    [Fact]
    public void Validate_ArbitraryFormat_CurrentBehavior_IsAccepted()
    {
        ScanRequestValidator validator = new ScanRequestValidator();

        ValidationResult result = validator.Validate(new ScanRequestDto(1, @"C:\evil", "evil"));

        Assert.True(result.IsValid);
    }
}
