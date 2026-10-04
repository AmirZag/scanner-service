using System.Collections;
using System.Globalization;
using System.Reflection;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Tests for RecentScansService.GetRecentScansAsync over a real temp export directory: file
/// enumeration limits (depth, count), extension filtering, multi-page grouping (including the
/// A-2 phantom-group defects), ordering, and content-type mapping.
/// </summary>
public sealed class RecentScansServiceTests : IAsyncLifetime
{
    private readonly ScanJobsTestHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GetRecentScans_SinglePdfFile_ReturnsOneGroup()
    {
        DateTime scanTime = new(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "memo.pdf", scanTime);
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(1, response.TotalGroups);
        Assert.Equal(10, response.RequestedCount);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("memo", group.ScanId);
        Assert.Equal("pdf", group.Format);
        Assert.Equal(1, group.FileCount);
        Assert.Equal(scanTime, group.Timestamp);
        ScanFileDto file = Assert.Single(group.Files);
        Assert.Equal("memo.pdf", file.Filename);
        Assert.Equal(Path.Combine(_harness.ExportDirectory, "memo.pdf"), file.RelativePath);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(scanTime, file.CreatedAt);
        Assert.True(file.SizeBytes > 0);
    }

    [Fact]
    public async Task GetRecentScans_NumberedImagePages_GroupIntoSingleMultiPageGroup()
    {
        // Per-page images follow the scanner's "base_yyyyMMdd_HHmmss_page" naming, so the page
        // suffix strips and the set groups under the shared datetime token.
        DateTime firstPageTime = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "pages_20261003_100000_1.jpg", firstPageTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "pages_20261003_100000_2.jpg", firstPageTime.AddMinutes(1));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "pages_20261003_100000_3.jpg", firstPageTime.AddMinutes(2));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(1, response.TotalGroups);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("pages_20261003_100000", group.ScanId);
        Assert.Equal("jpg", group.Format);
        Assert.Equal(3, group.FileCount);
        Assert.Equal(firstPageTime, group.Timestamp);
        Assert.Equal(
            new[] { "pages_20261003_100000_1.jpg", "pages_20261003_100000_2.jpg", "pages_20261003_100000_3.jpg" },
            group.Files.Select(file => file.Filename).ToArray());
        Assert.Equal(firstPageTime, group.Files[0].CreatedAt);
        Assert.Equal(firstPageTime.AddMinutes(1), group.Files[1].CreatedAt);
        Assert.Equal(firstPageTime.AddMinutes(2), group.Files[2].CreatedAt);
        Assert.All(group.Files, file => Assert.Equal("image/jpeg", file.ContentType));
    }

    // FIXED (Phase 2 Batch 1, audit A-2): ExtractScanId only strips a page suffix when the
    // remainder still ends with the embedded datetime token, so same-day scans keep their full
    // timestamp token and never collapse into one phantom group.
    [Fact]
    public async Task GetRecentScans_TwoSameDayPdfs_GroupSeparatelyByFullTimestamp()
    {
        DateTime firstScanTime = new(2026, 10, 3, 10, 10, 10, DateTimeKind.Utc);
        DateTime secondScanTime = new(2026, 10, 3, 11, 11, 11, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "scan_20261003_101010.pdf", firstScanTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "scan_20261003_111111.pdf", secondScanTime);
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(2, response.TotalGroups);
        ScanGroupDto firstGroup = Assert.Single(response.Groups, group => group.ScanId == "scan_20261003_101010");
        Assert.Equal(1, firstGroup.FileCount);
        Assert.Equal(firstScanTime, firstGroup.Timestamp);
        ScanGroupDto secondGroup = Assert.Single(response.Groups, group => group.ScanId == "scan_20261003_111111");
        Assert.Equal(1, secondGroup.FileCount);
        Assert.Equal(secondScanTime, secondGroup.Timestamp);
    }

    [Fact]
    public async Task GetRecentScans_SameBaseWithDifferentExtensions_FormSeparateGroups()
    {
        // Grouping keys carry the extension, so a PDF and a per-page PNG set sharing one scan's
        // datetime token never merge into a single group with a mixed file list.
        DateTime scanTime = new(2026, 10, 3, 10, 10, 10, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "scan_20261003_101010.pdf", scanTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "scan_20261003_101010_1.png", scanTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "scan_20261003_101010_2.png", scanTime);
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(2, response.TotalGroups);
        ScanGroupDto pdfGroup = Assert.Single(response.Groups, group => group.Format == "pdf");
        Assert.Equal("scan_20261003_101010", pdfGroup.ScanId);
        Assert.Equal(1, pdfGroup.FileCount);
        ScanGroupDto pngGroup = Assert.Single(response.Groups, group => group.Format == "png");
        Assert.Equal(2, pngGroup.FileCount);
    }

    [Fact]
    public async Task GetRecentScans_UserNamedFilesWithoutDatetimeToken_StaySeparateGroups()
    {
        // FIXED (Phase 2 Batch 1, audit A-2 second facet): names without the scanner's datetime
        // token are never treated as page-index candidates, so "report" and "report_2026" stay
        // two distinct documents instead of merging.
        DateTime firstReportTime = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime secondReportTime = new(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "report.pdf", firstReportTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "report_2026.pdf", secondReportTime);
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(2, response.TotalGroups);
        ScanGroupDto plainGroup = Assert.Single(response.Groups, group => group.ScanId == "report");
        Assert.Equal(1, plainGroup.FileCount);
        Assert.Equal("pdf", plainGroup.Format);
        ScanGroupDto yearSuffixGroup = Assert.Single(response.Groups, group => group.ScanId == "report_2026");
        Assert.Equal(1, yearSuffixGroup.FileCount);
    }

    [Fact]
    public async Task GetRecentScans_CountOne_ReturnsOnlyNewestGroup()
    {
        SeedThreeOrderedGroups();
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(1);

        Assert.Equal(3, response.TotalGroups);
        Assert.Equal(1, response.RequestedCount);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("diagram", group.ScanId);
    }

    [Fact]
    public async Task GetRecentScans_CountAboveGroupCount_ReturnsAllGroups()
    {
        SeedThreeOrderedGroups();
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(50);

        Assert.Equal(3, response.TotalGroups);
        Assert.Equal(50, response.RequestedCount);
        Assert.Equal(3, response.Groups.Count);
    }

    [Fact]
    public async Task GetRecentScans_ZeroCount_ReturnsNoGroups()
    {
        SeedThreeOrderedGroups();
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(0);

        Assert.Equal(3, response.TotalGroups);
        Assert.Equal(0, response.RequestedCount);
        Assert.Empty(response.Groups);
    }

    [Fact]
    public async Task GetRecentScans_EmptyDirectory_ReturnsNoGroups()
    {
        string emptyDirectory = _harness.CreateDirectory();
        await _harness.SetExportPathAsync(emptyDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(5);

        Assert.Equal(0, response.TotalGroups);
        Assert.Equal(5, response.RequestedCount);
        Assert.Empty(response.Groups);
    }

    [Fact]
    public async Task GetRecentScans_MissingDirectory_ReturnsNoGroups()
    {
        await _harness.SetExportPathAsync(Path.Combine(_harness.ExportDirectory, "never_created"));

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(5);

        Assert.Equal(0, response.TotalGroups);
        Assert.Equal(5, response.RequestedCount);
        Assert.Empty(response.Groups);
    }

    [Fact]
    public async Task GetRecentScans_FilesBeyondMaxDepth_AreExcluded()
    {
        // MaxDepth = 3: files at directory levels 0..3 are listed, level 4 is not.
        string level1 = Path.Combine(_harness.ExportDirectory, "level1");
        string level2 = Path.Combine(level1, "level2");
        string level3 = Path.Combine(level2, "level3");
        string level4 = Path.Combine(level3, "level4");
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "d0.pdf", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(level1, "d1.pdf", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(level2, "d2.pdf", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(level3, "d3.pdf", new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(level4, "d4.pdf", new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(4, response.TotalGroups);
        Assert.Equal(new[] { "d3", "d2", "d1", "d0" }, response.Groups.Select(group => group.ScanId).ToArray());
        Assert.DoesNotContain(response.Groups, group => group.ScanId == "d4");
    }

    [Fact]
    public async Task GetRecentScans_FilesAboveMaxFiles_CapsAtThousandFiles()
    {
        // MaxFiles = 1000: enumeration stops after 1000 supported files even though 1001 exist.
        // All files are pages of one scanner-shaped scan, so the cap shows up as a single
        // 1000-page group.
        DateTime bulkTime = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int index = 1; index <= 1001; index++)
        {
            ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "bulk_20260901_120000_" + index.ToString(CultureInfo.InvariantCulture) + ".jpg", bulkTime);
        }

        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(1, response.TotalGroups);
        Assert.Equal(10, response.RequestedCount);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("bulk_20260901_120000", group.ScanId);
        Assert.Equal(1000, group.FileCount);
        Assert.Equal(bulkTime, group.Timestamp);
    }

    [Fact]
    public async Task GetRecentScans_MultipleGroups_OrderedByTimestampDescending()
    {
        SeedThreeOrderedGroups();
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(new[] { "diagram", "photo", "memo" }, response.Groups.Select(group => group.ScanId).ToArray());
    }

    [Fact]
    public async Task GetRecentScans_SupportedExtensions_MapContentTypes()
    {
        DateTime baseTime = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "memo.pdf", baseTime);
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "photo.jpg", baseTime.AddMinutes(1));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "still.jpeg", baseTime.AddMinutes(2));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "diagram.png", baseTime.AddMinutes(3));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "pageone.tif", baseTime.AddMinutes(4));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "pagemore.tiff", baseTime.AddMinutes(5));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "poster.bmp", baseTime.AddMinutes(6));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(7, response.TotalGroups);
        var expectedContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["memo"] = "application/pdf",
            ["photo"] = "image/jpeg",
            ["still"] = "image/jpeg",
            ["diagram"] = "image/png",
            ["pageone"] = "image/tiff",
            ["pagemore"] = "image/tiff",
            ["poster"] = "image/bmp",
        };

        foreach (ScanGroupDto group in response.Groups)
        {
            Assert.Equal(expectedContentTypes[group.ScanId], group.Files[0].ContentType);
        }
    }

    [Fact]
    public async Task GetRecentScans_ZipFiles_AreExcluded()
    {
        // ContentTypes maps ".zip" for response zips, but the RecentScans whitelist
        // (SupportedExtensions.ScanFiles) does not include it, so archived scans never appear.
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "bundle.zip", new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(0, response.TotalGroups);
        Assert.Empty(response.Groups);
    }

    [Fact]
    public async Task GetRecentScans_UnsupportedExtensions_AreExcluded()
    {
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "notes.txt", new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "memo.pdf", new DateTime(2026, 10, 3, 8, 5, 0, DateTimeKind.Utc));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(1, response.TotalGroups);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("memo", group.ScanId);
    }

    [Fact]
    public async Task GetRecentScans_UppercaseExtension_IsIncludedWithLowercaseFormat()
    {
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "UPPER.PDF", new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(1, response.TotalGroups);
        ScanGroupDto group = Assert.Single(response.Groups);
        Assert.Equal("UPPER", group.ScanId);
        Assert.Equal("pdf", group.Format);
        Assert.Equal("application/pdf", group.Files[0].ContentType);
    }

    [Fact]
    public async Task GetRecentScans_WhitespaceConfiguredExportPath_FallsBackToDocumentsScans()
    {
        string documentsScans = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
        bool documentsScansPreExisted = Directory.Exists(documentsScans);
        try
        {
            await _harness.SetExportPathAsync("   ");

            RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(5);

            Assert.NotNull(response);
            Assert.Equal(5, response.RequestedCount);
            Assert.True(response.TotalGroups >= 0);
        }
        finally
        {
            if (!documentsScansPreExisted && Directory.Exists(documentsScans)
                && !Directory.EnumerateFileSystemEntries(documentsScans).Any())
            {
                Directory.Delete(documentsScans);
            }
        }
    }

    [Fact]
    public async Task GetRecentScans_FileLimitHitBetweenSubdirectories_StopsWalkingLaterSubdirectories()
    {
        // One slot short of the cap is filled by the first subdirectory's first file, so the
        // per-subdirectory guard must break out before the remaining subdirectories are walked.
        DateTime bulkTime = new(2026, 10, 3, 7, 0, 0, DateTimeKind.Utc);
        for (int index = 1; index <= ApplicationConstants.RecentScans.MaxFiles - 1; index++)
        {
            ScanJobsTestHarness.WriteScanFile(
                _harness.ExportDirectory,
                "bulk_20261003_070000_" + index.ToString(CultureInfo.InvariantCulture) + ".jpg",
                bulkTime);
        }

        string firstSubdirectory = Path.Combine(_harness.ExportDirectory, "sub1");
        ScanJobsTestHarness.WriteScanFile(firstSubdirectory, "sub_20261003_070001_1.jpg", bulkTime);
        ScanJobsTestHarness.WriteScanFile(firstSubdirectory, "sub_20261003_070001_2.jpg", bulkTime);
        string secondSubdirectory = Path.Combine(_harness.ExportDirectory, "sub2");
        ScanJobsTestHarness.WriteScanFile(secondSubdirectory, "other_20261003_070002_1.jpg", bulkTime);
        await _harness.SetExportPathAsync(_harness.ExportDirectory);

        RecentScansResponseDto response = await _harness.CreateRecentScansService().GetRecentScansAsync(10);

        Assert.Equal(2, response.TotalGroups);
        Assert.DoesNotContain(response.Groups, group => group.ScanId == "other_20261003_070002");
        ScanGroupDto bulkGroup = response.Groups.Single(group => group.ScanId == "bulk_20261003_070000");
        Assert.Equal(ApplicationConstants.RecentScans.MaxFiles - 1, bulkGroup.FileCount);
        ScanGroupDto truncatedGroup = Assert.Single(response.Groups, group => group.ScanId != "bulk_20261003_070000");
        Assert.Equal(1, truncatedGroup.FileCount);
    }

    // The enumeration internals are private and cannot be driven past the guards reachable
    // through GetRecentScansAsync (the public entry point pre-checks directory existence and
    // never exceeds MaxDepth); reflection is the only seam, and the pragma follows the
    // production pattern for justified Sonar suppressions.
#pragma warning disable S3011 // Reflection should not be used to increase usability of coded tests
    [Fact]
    public async Task FindFilesOptimized_DepthBeyondMaxDepth_ReturnsEmptyWithoutEnumerating()
    {
        RecentScansService service = _harness.CreateRecentScansService();
        int depthBeyondLimit = ApplicationConstants.RecentScans.MaxDepth + 1;

        Task enumerationTask = (Task)GetPrivateMethod("FindFilesOptimizedAsync")
            .Invoke(service, new object[] { _harness.ExportDirectory, depthBeyondLimit, CancellationToken.None })!;
        await enumerationTask;

        AssertEmptyTaskResult(enumerationTask);
    }

    [Fact]
    public async Task FindFilesOptimized_DirectoryAccessFails_SkipsDirectoryAndReturnsEmpty()
    {
        // A directory that vanished (or was never created) makes the enumeration throw a
        // DirectoryNotFoundException, which the service must swallow into an empty result.
        RecentScansService service = _harness.CreateRecentScansService();
        string missingDirectory = Path.Combine(_harness.ExportDirectory, "never_created_by_this_test");

        Task enumerationTask = (Task)GetPrivateMethod("FindFilesOptimizedAsync")
            .Invoke(service, new object[] { missingDirectory, 0, CancellationToken.None })!;
        await enumerationTask;

        AssertEmptyTaskResult(enumerationTask);
    }

    [Fact]
    public async Task FindFilesOptimized_UnexpectedEnumerationError_IsSwallowedIntoEmptyResult()
    {
        // An empty directory path makes the enumeration fail with an ArgumentException, which
        // matches neither the cancellation nor the IO filter and lands in the catch-all.
        RecentScansService service = _harness.CreateRecentScansService();

        Task enumerationTask = (Task)GetPrivateMethod("FindFilesOptimizedAsync")
            .Invoke(service, new object[] { string.Empty, 0, CancellationToken.None })!;
        await enumerationTask;

        AssertEmptyTaskResult(enumerationTask);
    }

    [Fact]
    public async Task GetSubdirectoriesSafe_DirectoryAccessFails_ReturnsEmptyList()
    {
        RecentScansService service = _harness.CreateRecentScansService();
        string missingDirectory = Path.Combine(_harness.ExportDirectory, "never_created_by_this_test");

        Task subdirectoriesTask = (Task)GetPrivateMethod("GetSubdirectoriesSafeAsync")
            .Invoke(service, new object[] { missingDirectory, CancellationToken.None })!;
        await subdirectoriesTask;

        AssertEmptyTaskResult(subdirectoriesTask);
    }

    private void SeedThreeOrderedGroups()
    {
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "memo.pdf", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "photo.jpg", new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc));
        ScanJobsTestHarness.WriteScanFile(_harness.ExportDirectory, "diagram.png", new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));
    }

    private static MethodInfo GetPrivateMethod(string methodName)
    {
        return typeof(RecentScansService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Method " + methodName + " was not found on RecentScansService.");
    }
#pragma warning restore S3011 // Reflection should not be used to increase usability of coded tests

    private static void AssertEmptyTaskResult(Task completedTask)
    {
        object? result = completedTask.GetType().GetProperty("Result")!.GetValue(completedTask);
        Assert.Empty((IEnumerable)result!);
    }
}
