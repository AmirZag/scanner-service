using System;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using Xunit;

namespace ScannerService.UnitTests.Domain;

public class ExportSettingTests
{
    [Fact]
    public void FreshExportSetting_HasDocumentedDefaults()
    {
        ExportSetting setting = new ExportSetting();

        Assert.Equal(ScannerConstants.ExportFormat.PDF, setting.Format);
        Assert.Equal(string.Empty, setting.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, setting.FileName);
        // UpdatedAt is stamped by a property initializer, so a fresh entity is never default(DateTime).
        Assert.True((DateTime.UtcNow - setting.UpdatedAt).Duration() <= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void CreateDefault_MatchesDocumentedExportDefaults()
    {
        DateTime before = DateTime.UtcNow;

        ExportSetting setting = ExportSetting.CreateDefault();

        DateTime after = DateTime.UtcNow;

        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFormat, setting.Format);
        Assert.Equal(string.Empty, setting.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, setting.FileName);
        Assert.Equal(DateTimeKind.Utc, setting.UpdatedAt.Kind);
        Assert.InRange(setting.UpdatedAt, before, after);
    }

    [Fact]
    public void Update_WithEachFieldProvided_AppliesOnlyThatField_AndRefreshesUpdatedAt()
    {
        ExportSetting setting = ExportSetting.CreateDefault();
        DateTime staleStamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        setting.UpdatedAt = staleStamp;
        setting.Update(format: ScannerConstants.ExportFormat.JPEG);

        Assert.Equal(ScannerConstants.ExportFormat.JPEG, setting.Format);
        Assert.Equal(string.Empty, setting.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, setting.FileName);
        Assert.True(setting.UpdatedAt > staleStamp);

        setting.UpdatedAt = staleStamp;
        setting.Update(exportPath: @"C:\Scans");

        Assert.Equal(ScannerConstants.ExportFormat.JPEG, setting.Format);
        Assert.Equal(@"C:\Scans", setting.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, setting.FileName);
        Assert.True(setting.UpdatedAt > staleStamp);

        setting.UpdatedAt = staleStamp;
        setting.Update(fileName: "report_{datetime}");

        Assert.Equal(ScannerConstants.ExportFormat.JPEG, setting.Format);
        Assert.Equal(@"C:\Scans", setting.ExportPath);
        Assert.Equal("report_{datetime}", setting.FileName);
        Assert.True(setting.UpdatedAt > staleStamp);
    }

    [Fact]
    public void Update_WithNullArguments_PreservesEveryValue_ButStillRefreshesUpdatedAt()
    {
        ExportSetting setting = new ExportSetting
        {
            Format = ScannerConstants.ExportFormat.PNG,
            ExportPath = @"D:\Archive",
            FileName = "custom_{datetime}",
            UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        setting.Update(null, null, null);

        Assert.Equal(ScannerConstants.ExportFormat.PNG, setting.Format);
        Assert.Equal(@"D:\Archive", setting.ExportPath);
        Assert.Equal("custom_{datetime}", setting.FileName);
        Assert.NotEqual(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), setting.UpdatedAt);
    }
}
