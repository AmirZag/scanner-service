using System;
using System.Collections.Generic;
using FluentValidation.Results;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ScannerSettingsValidatorTests
{
    [Fact]
    public void Validate_WithAnEmptyDeviceList_IsValid()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ValidationResult result = validator.Validate(new ScannerSettingsDto());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithWellFormedManualDevices_IsValid()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto dto = new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto>
            {
                new EsclManualDeviceSettingDto { Name = "HP Office", Address = "http://192.168.1.50:8080/eSCL" },
                new EsclManualDeviceSettingDto { Address = "192.168.1.51" },
                new EsclManualDeviceSettingDto { Name = null, Address = new string('a', 500) }
            }
        };

        ValidationResult result = validator.Validate(dto);

        Assert.True(result.IsValid);
    }

    // KNOWN BUG C-1b: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // The list rules are chained after NotNull with the default Continue cascade, so with
    // "esclManualDevices": null the NotNull failure does not stop the following Must predicates,
    // the first one dereferences the null list, and PUT /api/settings surfaces as a 500 instead
    // of the intended 400 - contradicting the validator's own NRE-avoidance design comment.
    [Fact]
    public void Validate_NullEsclManualDevices_CurrentBehavior_ThrowsNullReferenceException()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto dto = new ScannerSettingsDto();
        dto.EsclManualDevices = null!;

        Assert.Throws<NullReferenceException>(() =>
        {
            validator.Validate(dto);
        });
    }

    [Fact]
    public void Validate_WithMoreThan50ManualDevices_IsRejected()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        List<EsclManualDeviceSettingDto> devices = new List<EsclManualDeviceSettingDto>();
        for (int index = 0; index < 51; index++)
        {
            devices.Add(new EsclManualDeviceSettingDto { Address = $"http://192.168.1.{index}:8080/eSCL" });
        }

        ValidationResult result = validator.Validate(new ScannerSettingsDto { EsclManualDevices = devices });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.ErrorMessage.Contains("at most 50"));
    }

    [Fact]
    public void Validate_WithANullEntryInsideTheList_IsRejectedWithoutThrowing()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto dto = new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto>
            {
                new EsclManualDeviceSettingDto { Address = "http://192.168.1.50:8080/eSCL" },
                null!
            }
        };

        ValidationResult result = validator.Validate(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.ErrorMessage.Contains("must not contain null entries"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("192.168.1.50", true)]
    [InlineData("http://192.168.1.50:8080/eSCL", true)]
    [InlineData("http://host\u0007with\u0007bell", false)]
    public void Validate_Address_MustBeNonEmptyWithinLengthAndFreeOfControlCharacters(string address, bool expectedValid)
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto dto = new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto> { new EsclManualDeviceSettingDto { Address = address } }
        };

        ValidationResult result = validator.Validate(dto);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_Address_At500CharactersIsValid_At501IsRejected()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto atLimit = new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto> { new EsclManualDeviceSettingDto { Address = new string('a', 500) } }
        };
        ScannerSettingsDto overLimit = new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto> { new EsclManualDeviceSettingDto { Address = new string('a', 501) } }
        };

        Assert.True(validator.Validate(atLimit).IsValid);
        Assert.False(validator.Validate(overLimit).IsValid);
    }

    [Fact]
    public void Validate_Name_At200CharactersIsValid_At201OrControlCharactersIsRejected()
    {
        ScannerSettingsValidator validator = new ScannerSettingsValidator();

        ScannerSettingsDto nullName = NewSettingsWithDevice("http://192.168.1.50:8080/eSCL", null);
        ScannerSettingsDto atLimit = NewSettingsWithDevice("http://192.168.1.50:8080/eSCL", new string('n', 200));
        ScannerSettingsDto overLimit = NewSettingsWithDevice("http://192.168.1.50:8080/eSCL", new string('n', 201));
        ScannerSettingsDto withControlChar = NewSettingsWithDevice("http://192.168.1.50:8080/eSCL", "HP\u001Bescape");

        Assert.True(validator.Validate(nullName).IsValid);
        Assert.True(validator.Validate(atLimit).IsValid);
        Assert.False(validator.Validate(overLimit).IsValid);
        Assert.False(validator.Validate(withControlChar).IsValid);
    }

    private static ScannerSettingsDto NewSettingsWithDevice(string address, string? name)
    {
        return new ScannerSettingsDto
        {
            EsclManualDevices = new List<EsclManualDeviceSettingDto> { new EsclManualDeviceSettingDto { Name = name, Address = address } }
        };
    }
}
