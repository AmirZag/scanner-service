using FluentValidation;
using ScannerService.Application.DTOs;
using System.IO;

namespace ScannerService.Application.Validators;

public class ExportSettingValidator : AbstractValidator<ExportSettingDto>
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
            .When(x => !string.IsNullOrWhiteSpace(x.FileName))
            .Must(fileName =>
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return true;
                }
                // Check for invalid path characters
                return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            })
            .WithMessage("FileName contains invalid characters");

        RuleFor(x => x.ExportPath)
            .Must(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return true; // Empty is ok, will use default
                }

                // Check for invalid path characters
                if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    return false;
                }

                // Check if path is absolute
                if (!Path.IsPathRooted(path))
                {
                    return false;
                }

                return true;
            })
            .WithMessage("ExportPath must be a valid absolute path")
            .When(x => !string.IsNullOrWhiteSpace(x.ExportPath));
    }

    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase) { "PDF", "JPEG", "PNG", "TIFF", "MultiPageTIFF" };
}
