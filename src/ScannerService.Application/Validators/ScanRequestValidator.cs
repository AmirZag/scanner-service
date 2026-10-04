using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

public class ScanRequestValidator : AbstractValidator<ScanRequestDto>
{
    public ScanRequestValidator()
    {
        RuleFor(x => x.ProfileId)
            .GreaterThan(0)
            .WithMessage("ProfileId must be greater than 0");

        // Without this rule an arbitrary request string became the output file extension,
        // bypassing the whitelist the export-settings path enforces (audit C-1d). Null keeps the
        // export setting's configured format.
        RuleFor(x => x.Format)
            .Must(format => format == null || ExportSettingValidator.AllowedFormats.Contains(format))
            .WithMessage("Format must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF");
    }
}
