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

    // FIXED (Phase 2 Batch 1, audit A-3): the parser compares with StringComparison.OrdinalIgnoreCase,
    // matching the validators, so non-canonical casing maps to the requested bit depth instead of
    // silently scanning as Color.
    [Theory]
    [InlineData("grayscale", BitDepth.Grayscale)]
    [InlineData("GRAYSCALE", BitDepth.Grayscale)]
    [InlineData("blackandwhite", BitDepth.BlackAndWhite)]
    [InlineData("BlackAndWhite", BitDepth.BlackAndWhite)]
    public void ParseBitDepth_NonCanonicalCasing_MapsToRequestedBitDepth(string bitDepth, BitDepth expectedBitDepth)
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth(bitDepth);

        Assert.Equal(expectedBitDepth, parsed);
    }

    [Fact]
    public void ParseBitDepth_ArbitraryString_FallsBackToColor()
    {
        BitDepth parsed = ScannerServiceType.ParseBitDepth("16bit");

        Assert.Equal(BitDepth.Color, parsed);
    }
}
