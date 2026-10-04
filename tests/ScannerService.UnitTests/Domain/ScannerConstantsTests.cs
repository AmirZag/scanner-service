using ScannerService.Domain.Common;
using Xunit;

namespace ScannerService.UnitTests.Domain;

public class ScannerConstantsTests
{
    [Fact]
    public void ValueSets_MatchTheDocumentedCanonicalSpellings()
    {
        Assert.Equal("Feeder", ScannerConstants.PaperSource.Feeder);
        Assert.Equal("Glass", ScannerConstants.PaperSource.Glass);

        Assert.Equal("BlackAndWhite", ScannerConstants.BitDepth.BlackAndWhite);
        Assert.Equal("Grayscale", ScannerConstants.BitDepth.Grayscale);
        Assert.Equal("Color", ScannerConstants.BitDepth.Color);

        Assert.Equal("PDF", ScannerConstants.ExportFormat.PDF);
        Assert.Equal("JPEG", ScannerConstants.ExportFormat.JPEG);
        Assert.Equal("PNG", ScannerConstants.ExportFormat.PNG);
        Assert.Equal("TIFF", ScannerConstants.ExportFormat.TIFF);
        Assert.Equal("MultiPageTIFF", ScannerConstants.ExportFormat.MultiPageTIFF);

        Assert.Equal("A4", ScannerConstants.PageSize.A4);
        Assert.Equal("A5", ScannerConstants.PageSize.A5);
        Assert.Equal("Letter", ScannerConstants.PageSize.Letter);
        Assert.Equal("Legal", ScannerConstants.PageSize.Legal);

        Assert.Equal("Left", ScannerConstants.HorizontalAlign.Left);
        Assert.Equal("Center", ScannerConstants.HorizontalAlign.Center);
        Assert.Equal("Right", ScannerConstants.HorizontalAlign.Right);

        Assert.Equal("1:1", ScannerConstants.Scale.OneToOne);
        Assert.Equal("1:2", ScannerConstants.Scale.HalfSize);
        Assert.Equal("1:4", ScannerConstants.Scale.QuarterSize);
        Assert.Equal("1:8", ScannerConstants.Scale.EighthSize);

        Assert.Equal("Twain", ScannerConstants.Driver.Twain);
        Assert.Equal("Wia", ScannerConstants.Driver.Wia);
        Assert.Equal("Escl", ScannerConstants.Driver.Escl);
        Assert.Equal("Sane", ScannerConstants.Driver.Sane);
    }

    [Fact]
    public void TimeoutBudgets_MatchTheDocumentedDefaults()
    {
        Assert.Equal(15000, ScannerConstants.Timeouts.DefaultDriverTimeoutMs);
        Assert.Equal(8000, ScannerConstants.Timeouts.DefaultEsclSearchTimeoutMs);
        Assert.Equal(2000, ScannerConstants.Timeouts.EsclSearchMarginMs);
        Assert.Equal(60000, ScannerConstants.Timeouts.DefaultDriverCooldownMs);
        Assert.Equal(600000, ScannerConstants.Timeouts.DefaultDriverCooldownMaxMs);
        Assert.Equal(5000, ScannerConstants.Timeouts.DefaultScanQueueTimeoutMs);
        Assert.Equal(600000, ScannerConstants.Timeouts.DefaultScanOverallTimeoutMs);
        Assert.Equal(120000, ScannerConstants.Timeouts.DefaultScanNoProgressTimeoutMs);
        Assert.Equal(5000, ScannerConstants.Timeouts.DefaultShutdownTimeoutMs);
    }

    [Fact]
    public void ApplicationWideValues_MatchTheDocumentedDefaults()
    {
        Assert.Equal(1048576, ApplicationConstants.FileSizes.OneMegabyte);
        Assert.Equal(104857600, ApplicationConstants.FileSizes.OneHundredMegabytes);

        Assert.Equal(100, ApplicationConstants.RateLimit.DefaultMaxRequests);
        Assert.Equal(1, ApplicationConstants.RateLimit.DefaultWindowMinutes);
        Assert.Equal(1000, ApplicationConstants.RateLimit.CleanupThreshold);

        Assert.Equal(3, ApplicationConstants.RecentScans.MaxDepth);
        Assert.Equal(1000, ApplicationConstants.RecentScans.MaxFiles);

        Assert.Equal(100, ApplicationConstants.Ports.MaxPortSearchRange);

        Assert.Equal(85, ApplicationConstants.ExportDefaults.DefaultImageQuality);
        Assert.Equal(200, ApplicationConstants.ExportDefaults.DefaultResolution);
        Assert.Equal("PDF", ApplicationConstants.ExportDefaults.DefaultFormat);
        Assert.Equal("scan_{datetime}", ApplicationConstants.ExportDefaults.DefaultFileName);

        Assert.Equal("Glass", ApplicationConstants.ProfileDefaults.DefaultPaperSource);
        Assert.Equal("Color", ApplicationConstants.ProfileDefaults.DefaultBitDepth);
        Assert.Equal("A4", ApplicationConstants.ProfileDefaults.DefaultPageSize);
        Assert.Equal("Center", ApplicationConstants.ProfileDefaults.DefaultHorizontalAlign);
        Assert.Equal(200, ApplicationConstants.ProfileDefaults.DefaultResolution);
        Assert.Equal("1:1", ApplicationConstants.ProfileDefaults.DefaultScale);
        Assert.Equal(0, ApplicationConstants.ProfileDefaults.DefaultBrightness);
        Assert.Equal(0, ApplicationConstants.ProfileDefaults.DefaultContrast);

        Assert.Equal(1, ApplicationConstants.Database.DefaultExportSettingId);
        Assert.Equal(100, ApplicationConstants.Database.MaxNameLength);

        Assert.Equal("scanners", ApplicationConstants.RequestTimeoutPolicies.Scanners);
        Assert.Equal("scan", ApplicationConstants.RequestTimeoutPolicies.Scan);
    }

    [Fact]
    public void SupportedScanExtensions_MatchTheDocumentedListExactly()
    {
        string[] expected = { ".pdf", ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp" };

        Assert.Equal(expected, ApplicationConstants.SupportedExtensions.ScanFiles);
    }

    [Fact]
    public void EsclManualDevice_RecordStoresBothMembers_SupportsWithModification_AndValueEquality()
    {
        EsclManualDevice device = new EsclManualDevice("HP OfficeJet", "http://192.168.1.50:8080/eSCL");

        Assert.Equal("HP OfficeJet", device.Name);
        Assert.Equal("http://192.168.1.50:8080/eSCL", device.Address);

        EsclManualDevice sameValues = device with { };
        Assert.Equal(device, sameValues);

        EsclManualDevice renamed = device with { Name = "HP Copy Room" };
        Assert.NotEqual(device, renamed);
        Assert.Equal("HP Copy Room", renamed.Name);
        Assert.Equal("http://192.168.1.50:8080/eSCL", renamed.Address);
    }
}
