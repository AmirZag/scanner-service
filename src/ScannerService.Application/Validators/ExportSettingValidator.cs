using FluentValidation;
using ScannerService.Application.DTOs;
using System.IO;

namespace ScannerService.Application.Validators;

public class ExportSettingValidator : AbstractValidator<ExportSettingDto>
{
    public ExportSettingValidator()
    {
        RuleFor(x => x.Format)
            .Must(format => !string.IsNullOrWhiteSpace(format))
            .WithMessage("Format is required")
            .Must(format => AllowedFormats.Contains(format))
            .WithMessage("Format must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF");

        RuleFor(x => x.FileName)
            .Must(fileName => !string.IsNullOrWhiteSpace(fileName))
            .WithMessage("FileName is required")
            .Must(fileName => fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            .WithMessage("FileName contains invalid characters");

        RuleFor(x => x.ExportPath)
            .Must(path =>
            {
                // Empty is ok, will use default path
                if (string.IsNullOrWhiteSpace(path))
                {
                    return true;
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
            .WithMessage("ExportPath must be a valid absolute path or empty");
    }

    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase) { "PDF", "JPEG", "PNG", "TIFF", "MultiPageTIFF" };
}
