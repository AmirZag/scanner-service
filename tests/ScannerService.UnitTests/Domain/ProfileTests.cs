using System;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using Xunit;

namespace ScannerService.UnitTests.Domain;

public class ProfileTests
{
    [Fact]
    public void FreshProfile_HasDocumentedDefaults()
    {
        Profile profile = new Profile();

        Assert.Equal(string.Empty, profile.Name);
        Assert.Null(profile.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Glass, profile.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Color, profile.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A4, profile.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Center, profile.HorizontalAlign);
        Assert.Equal(ApplicationConstants.ProfileDefaults.DefaultResolution, profile.Resolution);
        Assert.Equal(ApplicationConstants.ProfileDefaults.DefaultScale, profile.Scale);
        Assert.Equal(ApplicationConstants.ProfileDefaults.DefaultBrightness, profile.Brightness);
        Assert.Equal(ApplicationConstants.ProfileDefaults.DefaultContrast, profile.Contrast);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultImageQuality, profile.ImageQuality);
        Assert.Equal(default(DateTime), profile.CreatedAt);
        Assert.Equal(default(DateTime), profile.UpdatedAt);
    }

    [Fact]
    public void Create_MapsEveryArgument_AndStampsUtcTimestamps()
    {
        DateTime before = DateTime.UtcNow;

        Profile profile = Profile.Create(
            "Invoices",
            "twain-hp-laserjet",
            ScannerConstants.PaperSource.Feeder,
            ScannerConstants.BitDepth.Grayscale,
            ScannerConstants.PageSize.Legal,
            ScannerConstants.HorizontalAlign.Left,
            300,
            ScannerConstants.Scale.HalfSize,
            10,
            -10,
            90);

        DateTime after = DateTime.UtcNow;

        Assert.Equal("Invoices", profile.Name);
        Assert.Equal("twain-hp-laserjet", profile.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Feeder, profile.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Grayscale, profile.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.Legal, profile.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Left, profile.HorizontalAlign);
        Assert.Equal(300, profile.Resolution);
        Assert.Equal(ScannerConstants.Scale.HalfSize, profile.Scale);
        Assert.Equal(10, profile.Brightness);
        Assert.Equal(-10, profile.Contrast);
        Assert.Equal(90, profile.ImageQuality);
        Assert.Equal(DateTimeKind.Utc, profile.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, profile.UpdatedAt.Kind);
        Assert.InRange(profile.CreatedAt, before, after);
        Assert.InRange(profile.UpdatedAt, before, after);
        Assert.True(profile.IsValidForScanning());
    }

    [Fact]
    public void Create_AcceptsNullDeviceId_AsValidDomainState()
    {
        Profile profile = Profile.Create("Draft", null!, "Glass", "Color", "A4", "Center", 200, "1:1", 0, 0, 85);

        Assert.Null(profile.DeviceId);
        Assert.False(profile.IsValidForScanning());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("wia-flatbed", true)]
    public void IsValidForScanning_RequiresNonEmptyDeviceId(string? deviceId, bool expected)
    {
        Profile profile = new Profile { DeviceId = deviceId };

        Assert.Equal(expected, profile.IsValidForScanning());
    }

    [Fact]
    public void Update_WithEveryFieldSet_AppliesAllElevenOptions_AndRefreshesUpdatedAt()
    {
        Profile profile = NewProfile();
        DateTime staleStamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        profile.UpdatedAt = staleStamp;

        profile.Update(new ProfileUpdateOptions
        {
            Name = "Renamed",
            DeviceId = "escl-http-9100",
            PaperSource = ScannerConstants.PaperSource.Feeder,
            BitDepth = ScannerConstants.BitDepth.BlackAndWhite,
            PageSize = ScannerConstants.PageSize.A5,
            HorizontalAlign = ScannerConstants.HorizontalAlign.Right,
            Resolution = 600,
            Scale = ScannerConstants.Scale.QuarterSize,
            Brightness = 25,
            Contrast = -25,
            ImageQuality = 100
        });

        Assert.Equal("Renamed", profile.Name);
        Assert.Equal("escl-http-9100", profile.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Feeder, profile.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.BlackAndWhite, profile.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A5, profile.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Right, profile.HorizontalAlign);
        Assert.Equal(600, profile.Resolution);
        Assert.Equal(ScannerConstants.Scale.QuarterSize, profile.Scale);
        Assert.Equal(25, profile.Brightness);
        Assert.Equal(-25, profile.Contrast);
        Assert.Equal(100, profile.ImageQuality);
        Assert.NotEqual(staleStamp, profile.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, profile.UpdatedAt.Kind);
        Assert.True(profile.UpdatedAt > staleStamp);
    }

    [Fact]
    public void Update_WithNullOptions_DoesNotTouchAnything()
    {
        Profile profile = NewProfile();

        profile.Update(null!);

        Assert.Equal("Baseline", profile.Name);
        Assert.Equal("device-baseline", profile.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Glass, profile.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Color, profile.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A4, profile.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Center, profile.HorizontalAlign);
        Assert.Equal(200, profile.Resolution);
        Assert.Equal(ScannerConstants.Scale.OneToOne, profile.Scale);
        Assert.Equal(0, profile.Brightness);
        Assert.Equal(0, profile.Contrast);
        Assert.Equal(85, profile.ImageQuality);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), profile.UpdatedAt);
    }

    [Fact]
    public void Update_WithAllNullOptions_LeavesUpdatedAtUnchanged()
    {
        Profile profile = NewProfile();
        DateTime unchanged = profile.UpdatedAt;

        profile.Update(new ProfileUpdateOptions());

        Assert.Equal("Baseline", profile.Name);
        Assert.Equal(200, profile.Resolution);
        Assert.Equal(unchanged, profile.UpdatedAt);
    }

    [Fact]
    public void Update_WithPartialOptions_AppliesOnlyProvidedFields()
    {
        Profile profile = NewProfile();

        profile.Update(new ProfileUpdateOptions
        {
            Name = "Only Name",
            Resolution = 400,
            Brightness = -5
        });

        Assert.Equal("Only Name", profile.Name);
        Assert.Equal(400, profile.Resolution);
        Assert.Equal(-5, profile.Brightness);
        Assert.Equal("device-baseline", profile.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Glass, profile.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Color, profile.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A4, profile.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Center, profile.HorizontalAlign);
        Assert.Equal(ScannerConstants.Scale.OneToOne, profile.Scale);
        Assert.Equal(0, profile.Contrast);
        Assert.Equal(85, profile.ImageQuality);
    }

    [Fact]
    public void Update_WithEmptyStringName_TreatsEmptyAsAValue_NotAsAbsence()
    {
        Profile profile = NewProfile();

        profile.Update(new ProfileUpdateOptions { Name = string.Empty });

        Assert.Equal(string.Empty, profile.Name);
    }

    [Fact]
    public void Update_WithSingleFieldChange_RefreshesUpdatedAt()
    {
        Profile profile = NewProfile();

        profile.Update(new ProfileUpdateOptions { Contrast = 7 });

        Assert.Equal(7, profile.Contrast);
        Assert.NotEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), profile.UpdatedAt);
    }

    private static Profile NewProfile()
    {
        return new Profile
        {
            Name = "Baseline",
            DeviceId = "device-baseline",
            PaperSource = ScannerConstants.PaperSource.Glass,
            BitDepth = ScannerConstants.BitDepth.Color,
            PageSize = ScannerConstants.PageSize.A4,
            HorizontalAlign = ScannerConstants.HorizontalAlign.Center,
            Resolution = 200,
            Scale = ScannerConstants.Scale.OneToOne,
            Brightness = 0,
            Contrast = 0,
            ImageQuality = 85,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
