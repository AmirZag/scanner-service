using NAPS2.Scan;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using ScannerServiceType = ScannerService.Infrastructure.Services.ScannerService;
using Xunit;

namespace ScannerService.UnitTests.ScannerCore;

/// <summary>
/// Tests for ScannerServiceType.ParsePaperSource, the pure string-to-NAPS2-enum mapping applied to the
/// stored Profile PaperSource value when building scan options. The parser is deliberately
/// case-insensitive (unlike ParseBitDepth, see audit finding A-3) and treats every non-feeder
/// value as the flatbed.
/// </summary>
public sealed class ParsePaperSourceTests
{
    [Theory]
    [InlineData(ScannerConstants.PaperSource.Feeder)]
    [InlineData("feeder")]
    [InlineData("FEEDER")]
    public void ParsePaperSource_FeederCasingVariants_ReturnsFeeder(string paperSource)
    {
        PaperSource parsed = ScannerServiceType.ParsePaperSource(paperSource);

        Assert.Equal(PaperSource.Feeder, parsed);
    }

    [Fact]
    public void ParsePaperSource_CanonicalGlass_ReturnsFlatbed()
    {
        PaperSource parsed = ScannerServiceType.ParsePaperSource(ScannerConstants.PaperSource.Glass);

        Assert.Equal(PaperSource.Flatbed, parsed);
    }

    [Fact]
    public void ParsePaperSource_ArbitraryString_ReturnsFlatbed()
    {
        PaperSource parsed = ScannerServiceType.ParsePaperSource("Desk");

        Assert.Equal(PaperSource.Flatbed, parsed);
    }
}
