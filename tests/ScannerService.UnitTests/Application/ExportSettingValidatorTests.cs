using System;
using FluentValidation.Results;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ExportSettingValidatorTests
{
    [Theory]
    [InlineData("PDF", "", "scan_{datetime}")]
    [InlineData("pdf", @"C:\Scans", "report_2026")]
    [InlineData("MultiPageTIFF", @"D:\Archive\", "page {datetime}")]
    [InlineData("JPEG", @"C:\Program Files\Scans", "scan")]
    public void Validate_WithDocumentedSettings_IsValid(string format, string exportPath, string fileName)
    {
        ExportSettingValidator validator = new ExportSettingValidator();

        ValidationResult result = validator.Validate(new ExportSettingDto(format, exportPath, fileName));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("DOCX", false)]
    [InlineData("EXE", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("pdf", true)]
    [InlineData("PNG", true)]
    public void Validate_Format_IsCheckedAgainstTheWhitelistCaseInsensitively(string format, bool expectedValid)
    {
        ExportSettingValidator validator = new ExportSettingValidator();

        ValidationResult result = validator.Validate(new ExportSettingDto(format, @"C:\Scans", "scan"));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("scan<1", false)]
    [InlineData("scan|name", false)]
    [InlineData("scan_20261003_142530", true)]
    [InlineData("scan {datetime}", true)]
    public void Validate_FileName_MustBeNonEmptyAndFreeOfInvalidFileNameCharacters(string fileName, bool expectedValid)
    {
        ExportSettingValidator validator = new ExportSettingValidator();

        ValidationResult result = validator.Validate(new ExportSettingDto("PDF", @"C:\Scans", fileName));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_ExportPath_AcceptsEmptyWhitespaceAndAbsolutePaths_RejectsRelativeAndInvalidPaths()
    {
        ExportSettingValidator validator = new ExportSettingValidator();

        Assert.True(validator.Validate(new ExportSettingDto("PDF", "", "scan")).IsValid);
        Assert.True(validator.Validate(new ExportSettingDto("PDF", " ", "scan")).IsValid);
        Assert.True(validator.Validate(new ExportSettingDto("PDF", @"C:\Scans", "scan")).IsValid);
        Assert.False(validator.Validate(new ExportSettingDto("PDF", "Scans", "scan")).IsValid);
        Assert.False(validator.Validate(new ExportSettingDto("PDF", @"C:\bad|path", "scan")).IsValid);
        // "C:relative\sub" is drive-relative, which Path.IsPathRooted reports as rooted, so the
        // validator's rootedness check accepts it.
        Assert.True(validator.Validate(new ExportSettingDto("PDF", @"C:relative\sub", "scan")).IsValid);
    }

    // KNOWN BUG C-1a: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // STJ does not enforce the non-nullable FileName annotation, so PUT /api/export-settings with
    // "fileName": null reaches the second Must predicate, dereferences null and surfaces as a 500
    // instead of the intended "FileName is required" validation failure.
    [Fact]
    public void Validate_NullFileName_CurrentBehavior_ThrowsNullReferenceException()
    {
        ExportSettingValidator validator = new ExportSettingValidator();

        ExportSettingDto dto = new ExportSettingDto("PDF", @"C:\Scans", null!);

        Assert.Throws<NullReferenceException>(() =>
        {
            validator.Validate(dto);
        });
    }
}
