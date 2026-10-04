using System;
using System.Collections.Generic;
using ScannerService.Domain.Common;

namespace ScannerService.Application.Validators;

/// <summary>
/// The single home of the profile allowed-value sets, sourced from <see cref="ScannerConstants"/>.
/// Both profile validators share these so a new value is added (or a casing tightened) in exactly
/// one place - the duplication is what let ParseBitDepth drift case-sensitively (audit A-3/C-47).
/// </summary>
internal static class ProfileAllowedValues
{
    public static readonly HashSet<string> PaperSource = new(StringComparer.OrdinalIgnoreCase)
    {
        ScannerConstants.PaperSource.Glass,
        ScannerConstants.PaperSource.Feeder
    };

    public static readonly HashSet<string> BitDepth = new(StringComparer.OrdinalIgnoreCase)
    {
        ScannerConstants.BitDepth.Color,
        ScannerConstants.BitDepth.Grayscale,
        ScannerConstants.BitDepth.BlackAndWhite
    };

    public static readonly HashSet<string> PageSize = new(StringComparer.OrdinalIgnoreCase)
    {
        ScannerConstants.PageSize.A4,
        ScannerConstants.PageSize.A5,
        ScannerConstants.PageSize.Letter,
        ScannerConstants.PageSize.Legal
    };

    public static readonly HashSet<string> HorizontalAlign = new(StringComparer.OrdinalIgnoreCase)
    {
        ScannerConstants.HorizontalAlign.Left,
        ScannerConstants.HorizontalAlign.Center,
        ScannerConstants.HorizontalAlign.Right
    };

    public static readonly HashSet<string> Scale = new(StringComparer.OrdinalIgnoreCase)
    {
        ScannerConstants.Scale.OneToOne,
        ScannerConstants.Scale.HalfSize,
        ScannerConstants.Scale.QuarterSize,
        ScannerConstants.Scale.EighthSize
    };
}
