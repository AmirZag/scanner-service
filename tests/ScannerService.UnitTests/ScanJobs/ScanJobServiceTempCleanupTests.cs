using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Tests for the process-wide temp-file tracking of ScanJobService: CleanupTempFile (delete and
/// untrack) and CleanupOldTempFiles (age-based sweep of tracked leftovers). The tracked state is
/// seeded through real multi-file scans, which produce tracked response zips.
/// </summary>
[Collection("BinState")]
public sealed class ScanJobServiceTempCleanupTests : IAsyncLifetime
{
    private readonly ScanJobsTestHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task CleanupTempFile_TrackedZipFile_DeletesFileThenFailsOnSecondCall()
    {
        string zipFilePath = await CreateTrackedZipAsync();

        ScanJobService service = _harness.CreateScanJobService();

        Result firstCleanup = service.CleanupTempFile(zipFilePath);
        Assert.True(firstCleanup.IsSuccess);
        Assert.False(File.Exists(zipFilePath));

        Result secondCleanup = service.CleanupTempFile(zipFilePath);
        Assert.False(secondCleanup.IsSuccess);
        Assert.Equal("File not found in temp files tracking", secondCleanup.Error);
    }

    [Fact]
    public void CleanupTempFile_UntrackedPath_FailsWithTrackingMessage()
    {
        string untrackedPath = Path.Combine(Path.GetTempPath(), "scan_never_tracked_by_any_scan.bin");

        Result result = _harness.CreateScanJobService().CleanupTempFile(untrackedPath);

        Assert.False(result.IsSuccess);
        Assert.Equal("File not found in temp files tracking", result.Error);
    }

    [Fact]
    public async Task CleanupOldTempFiles_DeletesOldTrackedFileAndKeepsRecentFile()
    {
        string oldZipPath = await CreateTrackedZipAsync();
        File.SetCreationTimeUtc(oldZipPath, DateTime.UtcNow - TimeSpan.FromHours(2));
        string recentZipPath = await CreateTrackedZipAsync();

        try
        {
            _harness.CreateScanJobService().CleanupOldTempFiles(TimeSpan.FromMinutes(30));

            Assert.False(File.Exists(oldZipPath));
            Result untrackedCheck = _harness.CreateScanJobService().CleanupTempFile(oldZipPath);
            Assert.False(untrackedCheck.IsSuccess);

            Assert.True(File.Exists(recentZipPath));
        }
        finally
        {
            if (File.Exists(recentZipPath))
            {
                _harness.CreateScanJobService().CleanupTempFile(recentZipPath);
            }
        }
    }

    [Fact]
    public async Task CleanupOldTempFiles_TrackedFileAlreadyGone_UntracksWithoutFailing()
    {
        string zipFilePath = await CreateTrackedZipAsync();
        File.Delete(zipFilePath);

        _harness.CreateScanJobService().CleanupOldTempFiles(TimeSpan.Zero);

        Result untrackedCheck = _harness.CreateScanJobService().CleanupTempFile(zipFilePath);
        Assert.False(untrackedCheck.IsSuccess);
        Assert.Equal("File not found in temp files tracking", untrackedCheck.Error);
    }

    [Fact]
    public async Task CleanupOldTempFiles_DeletionFails_SwallowsErrorAndKeepsFileTracked()
    {
        string zipFilePath = await CreateTrackedZipAsync();
        File.SetCreationTimeUtc(zipFilePath, DateTime.UtcNow - TimeSpan.FromHours(2));

        using (FileStream lockStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _harness.CreateScanJobService().CleanupOldTempFiles(TimeSpan.FromMinutes(30));

            // The sharing violation must not bubble out of the sweep, and the file must stay
            // tracked so a later sweep can retry.
            Assert.True(File.Exists(zipFilePath));
        }

        Result retryCleanup = _harness.CreateScanJobService().CleanupTempFile(zipFilePath);
        Assert.True(retryCleanup.IsSuccess);
        Assert.False(File.Exists(zipFilePath));
    }

    [Fact]
    public async Task CleanupTempFile_LockedFile_FailsAndStaysTrackedForRetry()
    {
        string zipFilePath = await CreateTrackedZipAsync();
        ScanJobService service = _harness.CreateScanJobService();

        using (FileStream lockStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Result failedCleanup = service.CleanupTempFile(zipFilePath);

            Assert.False(failedCleanup.IsSuccess);
            Assert.StartsWith("Failed to clean up temporary file:", failedCleanup.Error, StringComparison.Ordinal);
            Assert.True(File.Exists(zipFilePath));
        }

        // The failure must re-track the path: the retry after releasing the lock deletes it.
        Result retryCleanup = service.CleanupTempFile(zipFilePath);
        Assert.True(retryCleanup.IsSuccess);
        Assert.False(File.Exists(zipFilePath));
    }

    private async Task<string> CreateTrackedZipAsync()
    {
        _harness.Scanner.FileNamesToWrite = ["cleanup_page_1.jpg", "cleanup_page_2.jpg"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsSuccess);

        return result.Value!.FilePath!;
    }
}
