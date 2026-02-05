using FluentValidation;
using ScannerService.Application.DTOs;
using System.Linq;

namespace ScannerService.Application.Validators;

public class ExportSettingsDtoValidator : AbstractValidator<ExportSettingsDto>
{
    public ExportSettingsDtoValidator()
    {
        RuleFor(x => x.OutputFormat)
            .Must(x => new[] { "PDF", "JPEG", "PNG", "TIFF", "MultiPageTIFF" }.Contains(x))
            .WithMessage("OutputFormat must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF")
            .When(x => !string.IsNullOrWhiteSpace(x.OutputFormat));

        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("FileName is required")
            .When(x => !string.IsNullOrWhiteSpace(x.FileName));
    }
}