using FluentValidation;
using ScannerService.Application.DTOs;
using System.Linq;

namespace ScannerService.Application.Validators;

public class UpsertProfileValidator : AbstractValidator<UpsertProfile>
{
    public UpsertProfileValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Profile name is required")
            .MaximumLength(100).WithMessage("Profile name must not exceed 100 characters");

        RuleFor(x => x.PaperSource)
            .Must(x => new[] { "Glass", "Feeder" }.Contains(x))
            .WithMessage("PaperSource must be either 'Glass' or 'Feeder'");

        RuleFor(x => x.BitDepth)
            .Must(x => new[] { "Color", "Grayscale", "BlackAndWhite" }.Contains(x))
            .WithMessage("BitDepth must be 'Color', 'Grayscale', or 'BlackAndWhite'");

        RuleFor(x => x.PageSize)
            .Must(x => new[] { "A4", "A5", "Letter", "Legal" }.Contains(x))
            .WithMessage("PageSize must be one of: A4, A5, Letter, Legal");

        RuleFor(x => x.HorizontalAlign)
            .Must(x => new[] { "Left", "Center", "Right" }.Contains(x))
            .WithMessage("HorizontalAlign must be 'Left', 'Center', or 'Right'");

        RuleFor(x => x.Scale)
            .Must(x => new[] { "1:1", "1:2", "1:4", "1:8" }.Contains(x))
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
}