using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

internal class ExportSettingValidator : AbstractValidator<ExportSettingDto>
{
    public ExportSettingValidator()
    {
        RuleFor(x => x.Format)
            .Must(AllowedFormats.Contains)
            .WithMessage("Format must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF")
            .When(x => !string.IsNullOrWhiteSpace(x.Format));

        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("FileName is Required")
            .When(x => !string.IsNullOrWhiteSpace(x.FileName));
    }

    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase) { "PDF", "JPEG", "PNG", "TIFF", "MultiPageTIFF" };
}
