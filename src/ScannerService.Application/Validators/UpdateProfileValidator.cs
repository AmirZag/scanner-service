using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

public class UpdateProfileValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileValidator()
    {
        // Name is optional for updates, but if provided, must not be empty/whitespace and must not exceed 100 chars
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Profile name cannot be empty or whitespace")
            .MaximumLength(100).WithMessage("Profile name must not exceed 100 characters")
            .When(x => x.Name != null);

        // DeviceId is optional for updates, but if provided, must not be empty/whitespace
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("DeviceId cannot be empty or whitespace")
            .When(x => x.DeviceId != null);

        // If provided, validate against allowed values
        RuleFor(x => x.PaperSource)
            .Must(value => value != null && AllowedPaperSource.Contains(value))
            .WithMessage("PaperSource must be either 'Glass' or 'Feeder'")
            .When(x => x.PaperSource != null);

        RuleFor(x => x.BitDepth)
            .Must(value => value != null && AllowedBitDepth.Contains(value))
            .WithMessage("BitDepth must be 'Color', 'Grayscale', or 'BlackAndWhite'")
            .When(x => x.BitDepth != null);

        RuleFor(x => x.PageSize)
            .Must(value => value != null && AllowedPageSize.Contains(value))
            .WithMessage("PageSize must be one of: A4, A5, Letter, Legal")
            .When(x => x.PageSize != null);

        RuleFor(x => x.HorizontalAlign)
            .Must(value => value != null && AllowedHorizontalAlign.Contains(value))
            .WithMessage("HorizontalAlign must be 'Left', 'Center', or 'Right'")
            .When(x => x.HorizontalAlign != null);

        RuleFor(x => x.Scale)
            .Must(value => value != null && AllowedScale.Contains(value))
            .WithMessage("Scale must be one of: 1:1, 1:2, 1:4, 1:8")
            .When(x => x.Scale != null);

        RuleFor(x => x.Resolution)
            .InclusiveBetween(50, 1200)
            .WithMessage("Resolution must be between 50 and 1200 DPI")
            .When(x => x.Resolution != null);

        RuleFor(x => x.Brightness)
            .InclusiveBetween(-100, 100)
            .WithMessage("Brightness must be between -100 and 100")
            .When(x => x.Brightness != null);

        RuleFor(x => x.Contrast)
            .InclusiveBetween(-100, 100)
            .WithMessage("Contrast must be between -100 and 100")
            .When(x => x.Contrast != null);

        RuleFor(x => x.ImageQuality)
            .InclusiveBetween(1, 100)
            .WithMessage("ImageQuality must be between 1 and 100")
            .When(x => x.ImageQuality != null);
    }

    private static readonly HashSet<string> AllowedPaperSource = new(StringComparer.OrdinalIgnoreCase) { "Glass", "Feeder" };
    private static readonly HashSet<string> AllowedBitDepth = new(StringComparer.OrdinalIgnoreCase) { "Color", "Grayscale", "BlackAndWhite" };
    private static readonly HashSet<string> AllowedPageSize = new(StringComparer.OrdinalIgnoreCase) { "A4", "A5", "Letter", "Legal" };
    private static readonly HashSet<string> AllowedHorizontalAlign = new(StringComparer.OrdinalIgnoreCase) { "Left", "Center", "Right" };
    private static readonly HashSet<string> AllowedScale = new(StringComparer.OrdinalIgnoreCase) { "1:1", "1:2", "1:4", "1:8" };
}
