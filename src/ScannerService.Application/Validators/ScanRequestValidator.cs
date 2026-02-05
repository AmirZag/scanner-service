using FluentValidation;
using ScannerService.Application.DTOs;
using System.IO;
using System.Linq;

namespace ScannerService.Application.Validators;

public class ScanRequestValidator : AbstractValidator<ScanRequest>
{
    public ScanRequestValidator()
    {
        RuleFor(x => x.ProfileId)
            .GreaterThan(0)
            .WithMessage("ProfileId must be greater than 0");

        RuleFor(x => x.OutputFormat)
            .Must(x => x == null || new[] { "PDF", "JPEG", "PNG", "TIFF", "MultiPageTIFF" }.Contains(x))
            .WithMessage("OutputFormat must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF")
            .When(x => x.OutputFormat != null);

        RuleFor(x => x.OutputPath)
            .Must(path => path == null || Directory.Exists(Path.GetDirectoryName(path) ?? path))
            .WithMessage("OutputPath directory does not exist")
            .When(x => !string.IsNullOrWhiteSpace(x.OutputPath));
    }
}