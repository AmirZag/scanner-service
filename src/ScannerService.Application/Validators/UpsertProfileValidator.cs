using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

public class UpsertProfileValidator : AbstractValidator<UpsertProfileDto>
{
    public UpsertProfileValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Profile Name Is Required")
            .MaximumLength(100).WithMessage("Profile name must not exceed 100 characters");

        RuleFor(x => x.PaperSource)
            .Must(AllowedPaperSource.Contains)
            .WithMessage("PaperSource must be either 'Glass' or 'Feeder'");

        RuleFor(x => x.BitDepth)
            .Must(AllowedBitDepth.Contains)
            .WithMessage("BitDepth must be 'Color', 'Grayscale', or 'BlackAndWhite'");

        RuleFor(x => x.PageSize)
            .Must(AllowedPageSize.Contains)
            .WithMessage("PageSize must be one of: A4, A5, Letter, Legal");

        RuleFor(x => x.HorizontalAlign)
            .Must(AllowedHorizontalAlign.Contains)
            .WithMessage("HorizontalAlign must be 'Left', 'Center', or 'Right'");

        RuleFor(x => x.Scale)
            .Must(AllowedScale.Contains)
            .WithMessage("Scale must be one of: 1:1, 1:2, 1:4, 1:8");

        RuleFor(x => x.Resolution)
            .InclusiveBetween(50, 1200)
            .WithMessage("Resolution must be between 50 and 1200 DPI");

        RuleFor(x => x.Brightness)
            .InclusiveBetween(-100, 100)
            .WithMessage("Brightness must be between -100 and 100");

        RuleFor(x => x.Contrast)
            .InclusiveBetween(-100, 100)
            .WithMessage("Contrast must be between -100 and 100");

        RuleFor(x => x.ImageQuality)
            .InclusiveBetween(1, 100)
            .WithMessage("ImageQuality must be between 1 and 100");
    }

    private static readonly HashSet<string> AllowedPaperSource = new(StringComparer.OrdinalIgnoreCase) { "Glass", "Feeder" };

    private static readonly HashSet<string> AllowedBitDepth = new(StringComparer.OrdinalIgnoreCase) { "Color", "Grayscale", "BlackAndWhite" };

    private static readonly HashSet<string> AllowedPageSize = new(StringComparer.OrdinalIgnoreCase) { "A4", "A5", "Letter", "Legal" };

    private static readonly HashSet<string> AllowedHorizontalAlign = new(StringComparer.OrdinalIgnoreCase) { "Left", "Center", "Right" };

    private static readonly HashSet<string> AllowedScale = new(StringComparer.OrdinalIgnoreCase) { "1:1", "1:2", "1:4", "1:8" };

}
