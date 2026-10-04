using System;
using System.Linq;
using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

/// <summary>
/// Structural validation of the flat settings PUT body. Only shape rules live here (null
/// entries, empty/malformed device addresses, list size, string lengths); all numeric
/// range/cross-field validation is the existing ConfigurationValidator's job, run against
/// the full configuration — the single source of truth for ranges. A missing numeric field
/// deserializes as 0 and is rejected there with the exact allowed range in its message.
/// </summary>
public class ScannerSettingsValidator : AbstractValidator<ScannerSettingsDto>
{
    private const int MaxManualDeviceCount = 50;
    private const int MaxDeviceNameLength = 200;
    private const int MaxDeviceAddressLength = 500;

    public ScannerSettingsValidator()
    {
        // Rules are expressed on the LIST (not per-element) so a null entry — which fails the
        // null check — can never reach a property access (entry.Address) and NRE inside the
        // validator (which would surface as an opaque 500 instead of a 400).
        // The NotNull rule reports a missing list, and every Must is null-tolerant: with
        // FluentValidation's default Continue cascade the predicates still run on null, and an
        // unguarded dereference surfaced as an opaque 500 instead of a 400 (audit C-1b).
        RuleFor(dto => dto.EsclManualDevices)
            .NotNull()
            .WithMessage("EsclManualDevices is required (send an empty array if there are none)")
            .Must(devices => devices == null || devices.Count <= MaxManualDeviceCount)
            .WithMessage($"EsclManualDevices must contain at most {MaxManualDeviceCount} entries")
            .Must(devices => devices == null || devices.All(device => device != null))
            .WithMessage("EsclManualDevices must not contain null entries")
            .Must(devices => devices == null || devices.Where(device => device != null).All(device =>
                !string.IsNullOrWhiteSpace(device.Address)
                && device.Address.Length <= MaxDeviceAddressLength
                && device.Address.All(character => !char.IsControl(character))))
            .WithMessage($"Each EsclManualDevices entry needs a non-empty Address (max {MaxDeviceAddressLength} characters, no control characters)")
            .Must(devices => devices == null || devices.Where(device => device != null).All(device =>
                device.Name == null || device.Name.Length <= MaxDeviceNameLength && device.Name.All(character => !char.IsControl(character))))
            .WithMessage($"Each EsclManualDevices entry Name must be at most {MaxDeviceNameLength} characters with no control characters");
    }
}
