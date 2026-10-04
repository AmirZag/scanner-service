using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Orchestration tests for ScanJobService.StartScanJobAsync: profile lookup, export-setting
/// resolution, export-path validation, scanner delegation, and the multi-file response-zip path.
/// </summary>
/// <remarks>
/// Touches the process-wide static temp-file tracking state, so it must not run in parallel with
/// the other statics-touching classes.
/// </remarks>
[Collection("BinState")]
public sealed class ScanJobServiceScanTests : IAsyncLifetime
{
    private const string ZipFileNamePattern = "^scanned_documents_\\d{8}_\\d{6}\\.zip$";
    private const string ZipTempFileNamePattern = "^scan_[0-9a-fA-F-]{36}\\.zip$";

    private readonly ScanJobsTestHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task StartScanJob_SingleFileScan_ReturnsSuccessWithWrittenFileDetails()
    {
        _harness.Scanner.FileNamesToWrite = ["memo_scan.pdf"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsSuccess);
        ScanResultDto scanResult = result.Value!;
        string expectedFilePath = Path.Combine(_harness.ExportDirectory, "memo_scan.pdf");
        Assert.Equal(expectedFilePath, scanResult.FilePath);
        Assert.Equal("memo_scan.pdf", scanResult.FileName);
        Assert.Equal("application/pdf", scanResult.ContentType);
        Assert.True(scanResult.Duration >= TimeSpan.Zero);
        Assert.True(File.Exists(expectedFilePath));
        Assert.Equal("FAKE-SCAN-CONTENT:memo_scan.pdf", await File.ReadAllTextAsync(expectedFilePath));
    }

    [Fact]
    public async Task StartScanJob_MultiFileScan_CreatesTrackedZipWithAllFiles()
    {
        _harness.Scanner.FileNamesToWrite = ["page_1.jpg", "page_2.jpg", "page_3.jpg"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));
        ScanResultDto scanResult = result.Value!;

        try
        {
            Assert.True(result.IsSuccess);
            Assert.Equal("application/zip", scanResult.ContentType);
            Assert.Matches(ZipFileNamePattern, scanResult.FileName!);
            Assert.NotNull(scanResult.FilePath);
            Assert.True(File.Exists(scanResult.FilePath));
            // The response zip is built in %TEMP%, not next to the scanned files.
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.GetDirectoryName(scanResult.FilePath));
            Assert.Matches(ZipTempFileNamePattern, Path.GetFileName(scanResult.FilePath!));

            using ZipArchive archive = ZipFile.OpenRead(scanResult.FilePath!);
            Assert.Equal(3, archive.Entries.Count);
            List<string> entryNames = archive.Entries
                .Select(entry => entry.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(new[] { "page_1.jpg", "page_2.jpg", "page_3.jpg" }, entryNames);

            ZipArchiveEntry contentEntry = archive.Entries.Single(entry => entry.FullName == "page_2.jpg");
            await using Stream entryStream = contentEntry.Open();
            using StreamReader entryReader = new(entryStream);
            string entryContent = await entryReader.ReadToEndAsync();
            Assert.Equal("FAKE-SCAN-CONTENT:page_2.jpg", entryContent);
        }
        finally
        {
            if (scanResult?.FilePath is not null)
            {
                _harness.CreateScanJobService().CleanupTempFile(scanResult.FilePath);
            }
        }
    }

    [Fact]
    public async Task StartScanJob_EmptyFileList_CreatesEmptyZipAndReturnsSuccess()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));
        ScanResultDto scanResult = result.Value!;

        try
        {
            Assert.True(result.IsSuccess);
            Assert.Equal("application/zip", scanResult.ContentType);
            Assert.Matches(ZipFileNamePattern, scanResult.FileName!);

            using ZipArchive archive = ZipFile.OpenRead(scanResult.FilePath!);
            Assert.Empty(archive.Entries);
        }
        finally
        {
            if (scanResult?.FilePath is not null)
            {
                _harness.CreateScanJobService().CleanupTempFile(scanResult.FilePath);
            }
        }
    }

    [Fact]
    public async Task StartScanJob_ProfileNotFound_ReturnsFailureWithProfileMessage()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(424242));

        Assert.True(result.IsFailure);
        Assert.Equal("Profile 424242 not found", result.Error);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ProfileWithoutDevice_ReturnsFailureWithoutScanning()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: null);

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Profile does not have a scanner device assigned", result.Error);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ScannerFailure_IsPassedThroughAsFailure()
    {
        _harness.Scanner.ProgrammedResult = Result<List<string>>.Failure("device reported paper jam");
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("device reported paper jam", result.Error);
    }

    [Fact]
    public async Task StartScanJob_ScannerException_IsReturnedAsFailureMessage()
    {
        _harness.Scanner.ProgrammedException = new InvalidOperationException("driver crashed mid-scan");
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("driver crashed mid-scan", result.Error);
    }

    [Fact]
    public async Task StartScanJob_CancelledToken_RethrowsOperationCanceled()
    {
        // FIXED (Phase 2 Batch 2, audit A-11): a client-abort OperationCanceledException propagates
        // instead of being converted into a Failure result (and an Error-level "Scan failed" log
        // for a routine disconnect).
        _harness.Scanner.ThrowOperationCanceledWhenTokenCancelled = true;
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        using CancellationTokenSource cancelledSource = new();
        await cancelledSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _harness.CreateScanJobService().StartScanJobAsync(new ScanRequestDto(profile.Id), cancelledSource.Token));

        Assert.Empty(_harness.Scanner.WrittenFilePaths);
    }

    [Fact]
    public async Task StartScanJob_RequestExportPath_OverridesConfiguredExportPath()
    {
        _harness.Scanner.FileNamesToWrite = ["override_probe.pdf"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string requestDirectory = _harness.CreateDirectory();

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, requestDirectory));

        Assert.True(result.IsSuccess);
        ScanJobConfiguration configuration = _harness.Scanner.LastConfiguration!;
        Assert.NotNull(configuration);
        Assert.Equal(requestDirectory, configuration.ExportPath);
        Assert.True(File.Exists(Path.Combine(requestDirectory, "override_probe.pdf")));
    }

    [Fact]
    public async Task StartScanJob_ProfileAndSettingValues_FlowIntoScanConfiguration()
    {
        _harness.Scanner.FileNamesToWrite = ["propagation_probe.pdf"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.AddProfileAsync(Profile.Create(
            name: "Propagation Profile",
            deviceId: "escl-192-168-7-40",
            paperSource: ScannerConstants.PaperSource.Feeder,
            bitDepth: ScannerConstants.BitDepth.Grayscale,
            pageSize: ScannerConstants.PageSize.A4,
            horizontalAlign: ScannerConstants.HorizontalAlign.Center,
            resolution: 300,
            scale: ScannerConstants.Scale.OneToOne,
            brightness: 12,
            contrast: -7,
            imageQuality: 92));

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsSuccess);
        ScanJobConfiguration configuration = _harness.Scanner.LastConfiguration!;
        Assert.NotNull(configuration);
        Assert.Equal("escl-192-168-7-40", configuration.DeviceId);
        Assert.Equal(ScannerConstants.PaperSource.Feeder, configuration.PaperSource);
        Assert.Equal(ScannerConstants.BitDepth.Grayscale, configuration.BitDepth);
        Assert.Equal(300, configuration.Resolution);
        Assert.Equal(12, configuration.Brightness);
        Assert.Equal(-7, configuration.Contrast);
        Assert.Equal(92, configuration.ImageQuality);
        Assert.Equal("PDF", configuration.Format);
        Assert.Equal(_harness.ExportDirectory, configuration.ExportPath);
        Assert.Equal("scan_{datetime}", configuration.FileName);
    }

    [Fact]
    public async Task StartScanJob_RequestFormat_OverridesConfiguredFormat()
    {
        _harness.Scanner.FileNamesToWrite = ["format_probe.pdf"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, Format: "JPEG"));

        Assert.True(result.IsSuccess);
        ScanJobConfiguration configuration = _harness.Scanner.LastConfiguration!;
        Assert.NotNull(configuration);
        Assert.Equal("JPEG", configuration.Format);
        Assert.Equal(_harness.ExportDirectory, configuration.ExportPath);
    }

    [Fact]
    public async Task StartScanJob_EmptyConfiguredExportPath_FallsBackToDocumentsScans()
    {
        string documentsScans = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
        _harness.Scanner.FileNamesToWrite = ["fallback_probe.pdf"];
        // The seeded ExportSetting row keeps its empty ExportPath; the request carries none either.
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(documentsScans, _harness.Scanner.LastConfiguration!.ExportPath);
        Assert.True(Directory.Exists(documentsScans));

        // Leave no trace: remove the fake's files, then the fallback directory only if it is empty.
        try
        {
            foreach (string writtenPath in _harness.Scanner.WrittenFilePaths)
            {
                File.Delete(writtenPath);
            }

            if (!Directory.EnumerateFileSystemEntries(documentsScans).Any())
            {
                Directory.Delete(documentsScans);
            }
        }
        catch (IOException)
        {
            // Teardown is best-effort: a lingering lock must not fail an otherwise passing test.
        }
    }

    [Fact]
    public async Task StartScanJob_RelativeExportPath_ReturnsAbsolutePathFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, @"relative\scans"));

        Assert.True(result.IsFailure);
        Assert.Equal("Export path must be an absolute path", result.Error);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ExportPathWithInvalidCharacters_ReturnsInvalidCharactersFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, @"C:\scans\bad|name"));

        Assert.True(result.IsFailure);
        Assert.Equal("Export path contains invalid characters", result.Error);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ExportSettingsUnavailable_ReturnsFailureWithoutScanning()
    {
        FakeExportSettingRepository failingRepository = new(Result<ExportSettingDto>.Failure("settings store unavailable"));
        ScanJobService service = new(
            new Context(_harness.Options),
            _harness.Scanner,
            failingRepository,
            NullLogger<ScanJobService>.Instance);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await service.StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to retrieve export settings: settings store unavailable", result.Error);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_MissingSourceFile_TracksPartialZipSoSweepRemovesIt()
    {
        // FIXED (Phase 2 Batch 2, audit A-6): the zip is tracked before the archive block, so an
        // entry failure mid-archive leaves the partial zip ON the cleanup sweep's radar and the
        // next CleanupOldTempFiles removes it instead of orphaning it invisibly.
        string tempRoot = Path.GetTempPath();
        HashSet<string> zipsBefore = new(Directory.GetFiles(tempRoot, "scan_*.zip"), StringComparer.Ordinal);
        _harness.Scanner.FileNamesToWrite = ["page_1.jpg", "page_2.jpg", "page_3.jpg"];
        _harness.Scanner.OmitWriting("page_2.jpg");
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id));

        Assert.True(result.IsFailure);
        Assert.Contains("page_2.jpg", result.Error!, StringComparison.Ordinal);

        List<string> newZipPaths = Directory.GetFiles(tempRoot, "scan_*.zip")
            .Where(path => !zipsBefore.Contains(path))
            .ToList();
        string partialZipPath = Assert.Single(newZipPaths);

        try
        {
            Assert.True(File.Exists(partialZipPath));

            _harness.CreateScanJobService().CleanupOldTempFiles(TimeSpan.Zero);
            Assert.False(File.Exists(partialZipPath), "Expected the tracked partial zip to be swept by CleanupOldTempFiles");
        }
        finally
        {
            File.Delete(partialZipPath);
        }
    }

    [Fact]
    public async Task StartScanJob_NonExistingExportPath_IsCreatedAndScanProceeds()
    {
        _harness.Scanner.FileNamesToWrite = ["created_path_probe.pdf"];
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string newExportDirectory = Path.Combine(_harness.CreateDirectory(), "made_by_validation");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, newExportDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(newExportDirectory));
        Assert.True(File.Exists(Path.Combine(newExportDirectory, "created_path_probe.pdf")));
    }

    [Fact]
    public async Task StartScanJob_ExportDirectoryCreationDenied_ReturnsAccessDeniedFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string deniedDirectory = _harness.CreateDirectory();
        DenyDirectoryRights(deniedDirectory, FileSystemRights.CreateDirectories);

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, Path.Combine(deniedDirectory, "blocked_child")));

        Assert.True(result.IsFailure);
        Assert.Contains("Cannot create export directory - access denied", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ExportPathOccupiedByFile_ReturnsCreateFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string occupiedPath = Path.Combine(_harness.CreateDirectory(), "occupied.bin");
        File.WriteAllText(occupiedPath, "not a directory");

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, occupiedPath));

        Assert.True(result.IsFailure);
        Assert.Contains("Cannot create export directory:", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ExportDirectoryWriteDenied_ReturnsWriteAccessDeniedFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string deniedDirectory = _harness.CreateDirectory();
        DenyDirectoryRights(deniedDirectory, FileSystemRights.CreateFiles | FileSystemRights.Write);

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, deniedDirectory));

        Assert.True(result.IsFailure);
        Assert.Contains("Cannot write to export directory - access denied", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    [Fact]
    public async Task StartScanJob_ExportDirectoryWriteFailsWithIoError_ReturnsIoFailure()
    {
        await _harness.SetExportPathAsync(_harness.ExportDirectory);
        Profile profile = await _harness.SeedProfileAsync(deviceId: "escl-device-01");
        string missingTarget = Path.Combine(_harness.CreateDirectory(), "never_created_target");
        string junctionPath = Path.Combine(_harness.CreateDirectory(), "broken_link");
        CreateDanglingJunction(junctionPath, missingTarget);
        Assert.True(Directory.Exists(junctionPath));

        Result<ScanResultDto> result = await _harness.CreateScanJobService()
            .StartScanJobAsync(new ScanRequestDto(profile.Id, junctionPath));

        Assert.True(result.IsFailure);
        Assert.Contains("Cannot write to export directory:", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Scanner.ExecuteCallCount);
    }

    /// <summary>
    /// Adds a deny ACE for the current user to the directory's DACL. Deny entries win over
    /// allow entries regardless of elevation, so the simulated access failure is deterministic.
    /// The harness deletes the directory afterwards, which only needs the DELETE right that is
    /// left untouched.
    /// </summary>
    private static void DenyDirectoryRights(string directoryPath, FileSystemRights deniedRights)
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
        DirectorySecurity security = directoryInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            deniedRights,
            AccessControlType.Deny));
        directoryInfo.SetAccessControl(security);
    }

    /// <summary>
    /// Creates an NTFS junction whose target does not exist (mklink /J is unprivileged).
    /// Directory.Exists reports the dangling junction itself as existing, while any file
    /// operation below it fails with an IOException.
    /// </summary>
    private static void CreateDanglingJunction(string linkPath, string missingTargetPath)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + linkPath + "\" \"" + missingTargetPath + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
    }
}
