using NAPS2.Images;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for ScannerServiceType.ParseBitDepth, the pure string-to-NAPS2-enum mapping applied to the
/// stored Profile BitDepth value when building scan options. The production profile validators
/// accept the canonical values case-insensitively, so the casing behavior of this parser is the
/// contract that matters and is pinned here.
/// </summary>
public sealed class ParseBitDepthTests
{
    [Theory]
    [InlineData(ScannerConstants.BitDepth.Color, BitDepth.Color)]
    [InlineData(ScannerConstants.BitDepth.Grayscale, BitDepth.Grayscale)]
    [InlineData(ScannerConstants.BitDepth.BlackAndWhite, BitDepth.BlackAndWhite)]
    public void ParseBitDepth_CanonicalCasing_ReturnsMatchingBitDepth(string bitDepth, BitDepth expectedBitDepth)
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth(bitDepth);

        Assert.Equal(expectedBitDepth, parsed);
    }

    // KNOWN BUG A-3: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // The profile validators accept BitDepth case-insensitively and store the raw string, but this
    // parser is a case-sensitive constant-pattern switch: "grayscale" falls through to the default
    // arm and silently scans as Color. After the fix it must map to BitDepth.Grayscale.
    [Fact]
    public void ParseBitDepth_LowercaseGrayscale_CurrentBehavior_ReturnsColor()
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth("grayscale");

        Assert.Equal(BitDepth.Color, parsed);
    }

    // KNOWN BUG A-3: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // Same case-sensitivity defect as the lowercase grayscale pin: "blackandwhite" also falls
    // through to Color. After the fix it must map to BitDepth.BlackAndWhite.
    [Fact]
    public void ParseBitDepth_LowercaseBlackAndWhite_CurrentBehavior_ReturnsColor()
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth("blackandwhite");

        Assert.Equal(BitDepth.Color, parsed);
    }

    [Fact]
    public void ParseBitDepth_ArbitraryString_FallsBackToColor()
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth("16bit");

        Assert.Equal(BitDepth.Color, parsed);
    }
}
