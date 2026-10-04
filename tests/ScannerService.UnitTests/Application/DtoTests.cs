using System;
using System.Collections.Generic;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class DtoTests
{
    [Fact]
    public void ScannerSettingsDto_Defaults_MatchTheConfigurationDefaults()
    {
        ScannerSettingsDto dto = new ScannerSettingsDto();

        Assert.Equal(5000, dto.StatusCheckInterval);
        Assert.Equal(2000, dto.HttpTimeout);
        Assert.Equal(2000, dto.StartupDelay);
        Assert.Equal(15000, dto.DriverTimeoutMs);
        Assert.Equal(8000, dto.EsclSearchTimeoutMs);
        Assert.Equal(2000, dto.EsclSearchMarginMs);
        Assert.Equal(60000, dto.DriverCooldownMs);
        Assert.Equal(600000, dto.DriverCooldownMaxMs);
        Assert.Equal(5000, dto.ScanQueueTimeoutMs);
        Assert.Equal(600000, dto.ScanOverallTimeoutMs);
        Assert.Equal(120000, dto.ScanNoProgressTimeoutMs);
        Assert.Equal(5000, dto.ShutdownTimeoutMs);
        Assert.Equal(25, dto.ScannersRequestTimeoutSeconds);
        Assert.Equal(660, dto.ScanRequestTimeoutSeconds);
        Assert.NotNull(dto.EsclManualDevices);
        Assert.Empty(dto.EsclManualDevices);
    }

    [Fact]
    public void UpsertProfileDto_OptionalMembers_HaveTheDocumentedDefaults()
    {
        UpsertProfileDto dto = new UpsertProfileDto("Solo");

        Assert.Null(dto.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Glass, dto.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Color, dto.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A4, dto.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Center, dto.HorizontalAlign);
        Assert.Equal(200, dto.Resolution);
        Assert.Equal(ScannerConstants.Scale.OneToOne, dto.Scale);
        Assert.Equal(0, dto.Brightness);
        Assert.Equal(0, dto.Contrast);
        Assert.Equal(85, dto.ImageQuality);
    }

    [Fact]
    public void PatchDtos_AllMembersDefaultToNull()
    {
        UpdateProfileDto update = new UpdateProfileDto();
        ScanRequestDto request = new ScanRequestDto(1);

        Assert.Null(update.Name);
        Assert.Null(update.DeviceId);
        Assert.Null(update.PaperSource);
        Assert.Null(update.BitDepth);
        Assert.Null(update.Resolution);
        Assert.Null(update.PageSize);
        Assert.Null(update.HorizontalAlign);
        Assert.Null(update.Scale);
        Assert.Null(update.Brightness);
        Assert.Null(update.Contrast);
        Assert.Null(update.ImageQuality);
        Assert.Equal(1, request.ProfileId);
        Assert.Null(request.ExportPath);
        Assert.Null(request.Format);
    }

    [Fact]
    public void ScanResultDto_Factories_ProduceTheDocumentedShapes()
    {
        TimeSpan duration = TimeSpan.FromSeconds(12.5);

        ScanResultDto success = ScanResultDto.Successful(@"C:\Scans\scan.pdf", "scan.pdf", "application/pdf", duration);
        ScanResultDto failure = ScanResultDto.Failed("device offline", duration);

        Assert.True(success.Success);
        Assert.Equal(@"C:\Scans\scan.pdf", success.FilePath);
        Assert.Equal("scan.pdf", success.FileName);
        Assert.Equal("application/pdf", success.ContentType);
        Assert.Null(success.ErrorMessage);
        Assert.Equal(duration, success.Duration);
        Assert.False(failure.Success);
        Assert.Null(failure.FilePath);
        Assert.Null(failure.FileName);
        Assert.Null(failure.ContentType);
        Assert.Equal("device offline", failure.ErrorMessage);
        Assert.Equal(duration, failure.Duration);
    }

    [Fact]
    public void DetailedApiHealthCheckDto_Factories_ProduceHealthyAndUnhealthyShapes()
    {
        Dictionary<string, bool> dependencies = new Dictionary<string, bool> { ["api"] = true };

        DetailedApiHealthCheckDto healthy = DetailedApiHealthCheckDto.Healthy("1.2.3.4", dependencies);
        DetailedApiHealthCheckDto unhealthy = DetailedApiHealthCheckDto.Unhealthy("1.2.3.4", dependencies, "corr-1");

        Assert.True(healthy.IsHealthy);
        Assert.Equal("1.2.3.4", healthy.Version);
        Assert.NotNull(healthy.Dependencies);
        Assert.Null(healthy.CorrelationId);
        Assert.False(unhealthy.IsHealthy);
        Assert.Equal("corr-1", unhealthy.CorrelationId);
    }

    [Fact]
    public void RecentScansRecords_ConstructWithTheirMembers_AndSupportWithModification()
    {
        DateTime capturedAt = new DateTime(2026, 10, 3, 14, 25, 30, DateTimeKind.Utc);
        ScanFileDto file = new ScanFileDto("scan_1.jpg", @"C:\Scans\scan_1.jpg", "image/jpeg", 2048, capturedAt);
        ScanGroupDto group = new ScanGroupDto("scan_20261003_142530", capturedAt, "jpg", 1, new List<ScanFileDto> { file });

        Assert.Equal("scan_1.jpg", file.Filename);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(2048, file.SizeBytes);
        Assert.Equal(1, group.FileCount);
        Assert.Single(group.Files);

        RecentScansResponseDto response = new RecentScansResponseDto(1, 5, new List<ScanGroupDto> { group });
        Assert.Equal(1, response.TotalGroups);
        Assert.Equal(5, response.RequestedCount);
        Assert.Single(response.Groups);

        ScanGroupDto renamedGroup = group with { ScanId = "scan_20261003_150000" };
        Assert.NotEqual(group, renamedGroup);
        Assert.Equal("scan_20261003_150000", renamedGroup.ScanId);
        Assert.Equal(capturedAt, renamedGroup.Timestamp);
    }

    [Fact]
    public void ProfileDto_SupportsWithModification_WhilePreservingUnchangedMembers()
    {
        DateTime stamp = new DateTime(2026, 10, 3, 14, 25, 30, DateTimeKind.Utc);

        ProfileDto profile = new ProfileDto(
            1,
            "Invoices",
            "wia-flatbed",
            ScannerConstants.PaperSource.Glass,
            ScannerConstants.BitDepth.Color,
            ScannerConstants.PageSize.A4,
            ScannerConstants.HorizontalAlign.Center,
            200,
            ScannerConstants.Scale.OneToOne,
            0,
            0,
            85,
            stamp,
            stamp);

        ProfileDto renamed = profile with { Name = "Invoices 2026" };

        Assert.Equal("Invoices", profile.Name);
        Assert.Equal("Invoices 2026", renamed.Name);
        Assert.Equal(profile.Id, renamed.Id);
        Assert.Equal(profile.Resolution, renamed.Resolution);
        Assert.Equal(profile.CreatedAt, renamed.CreatedAt);
        Assert.NotEqual(profile, renamed);
    }

    [Fact]
    public void ScanJobConfiguration_Defaults_MatchTheDocumentedValues()
    {
        ScanJobConfiguration configuration = new ScanJobConfiguration();

        Assert.Equal(string.Empty, configuration.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Glass, configuration.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Color, configuration.BitDepth);
        Assert.Equal(200, configuration.Resolution);
        Assert.Equal(0, configuration.Brightness);
        Assert.Equal(0, configuration.Contrast);
        Assert.Equal(85, configuration.ImageQuality);
        Assert.Equal(ScannerConstants.ExportFormat.PDF, configuration.Format);
        Assert.Equal(string.Empty, configuration.ExportPath);
        Assert.Equal("scan_{datetime}", configuration.FileName);
    }

    [Fact]
    public void TrivialPositionalRecords_ExposeTheirPositionalMembers()
    {
        ScannerDto scanner = new ScannerDto("wia-hp", "HP LaserJet", "Wia");
        ApiHealthCheckDto health = new ApiHealthCheckDto(true, "1.2.3.4");
        ExportSettingDto exportSetting = new ExportSettingDto("PDF", @"C:\Scans", "scan_{datetime}");
        SettingsUpdateResponseDto updateResponse = new SettingsUpdateResponseDto(true, "restart in progress");
        DependencyHealthDto dependency = new DependencyHealthDto("database", true);

        Assert.Equal("wia-hp", scanner.Id);
        Assert.Equal("HP LaserJet", scanner.Name);
        Assert.Equal("Wia", scanner.Driver);
        Assert.True(health.IsRunning);
        Assert.Equal("1.2.3.4", health.Version);
        Assert.Equal("PDF", exportSetting.Format);
        Assert.Equal(@"C:\Scans", exportSetting.ExportPath);
        Assert.Equal("scan_{datetime}", exportSetting.FileName);
        Assert.True(updateResponse.Restarting);
        Assert.Equal("restart in progress", updateResponse.Warning);
        Assert.Equal("database", dependency.Name);
        Assert.True(dependency.IsHealthy);
    }
}
