using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using ScannerService.Infrastructure.Services;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Per-test harness: an isolated SQLite in-memory database on a shared open connection (the
/// seeded ExportSetting row included), a fake scanner, and uniquely named temp directories that
/// are removed again on disposal. Every test gets its own instance, so tests stay independent.
/// </summary>
internal sealed class ScanJobsTestHarness : IAsyncDisposable
{
    private readonly List<string> _createdDirectories = [];
    private readonly SqliteConnection _connection;

    public ScanJobsTestHarness()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        ExportDirectory = CreateUniqueDirectory();
        Scanner = new FakeScannerService();
    }

    public string ExportDirectory { get; }

    public FakeScannerService Scanner { get; }

    public DbContextOptions<Context> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        Options = new DbContextOptionsBuilder<Context>()
            .UseSqlite(_connection)
            .Options;

        await using var context = new Context(Options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task<Profile> AddProfileAsync(Profile profile)
    {
        await using var context = new Context(Options);
        context.Profiles.Add(profile);
        await context.SaveChangesAsync();

        return profile;
    }

    public Task<Profile> SeedProfileAsync(string? deviceId)
    {
        // The unique suffix keeps repeated seedings (one per test, each with its own harness but
        // potentially a shared named in-memory database) clear of the unique Profiles.Name index.
        Profile profile = Profile.Create(
            name: "ScanJob Test Profile " + Guid.NewGuid().ToString("N"),
            deviceId: deviceId,
            paperSource: ScannerConstants.PaperSource.Glass,
            bitDepth: ScannerConstants.BitDepth.Color,
            pageSize: ScannerConstants.PageSize.A4,
            horizontalAlign: ScannerConstants.HorizontalAlign.Center,
            resolution: 200,
            scale: ScannerConstants.Scale.OneToOne,
            brightness: 0,
            contrast: 0,
            imageQuality: 85);

        return AddProfileAsync(profile);
    }

    public async Task SetExportPathAsync(string exportPath)
    {
        await using var context = new Context(Options);
        ExportSetting exportSetting = await context.ExportSettings.SingleAsync(
            entity => entity.Id == ApplicationConstants.Database.DefaultExportSettingId);

        exportSetting.ExportPath = exportPath;
        await context.SaveChangesAsync();
    }

    public string CreateDirectory() => CreateUniqueDirectory();

    public ScanJobService CreateScanJobService()
    {
        IExportSettingRepository exportSettingRepository = new ExportSettingRepository(
            new Context(Options),
            NullLogger<ExportSettingRepository>.Instance);

        return new ScanJobService(
            new Context(Options),
            Scanner,
            exportSettingRepository,
            NullLogger<ScanJobService>.Instance);
    }

    public RecentScansService CreateRecentScansService()
    {
        return new RecentScansService(
            new Context(Options),
            NullLogger<RecentScansService>.Instance);
    }

    /// <summary>
    /// Writes a small real file with distinctive content and pins its creation timestamp, so
    /// grouping and ordering assertions are deterministic.
    /// </summary>
    public static string WriteScanFile(string directoryPath, string fileName, DateTime creationTimeUtc)
    {
        Directory.CreateDirectory(directoryPath);
        string filePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, "SCAN-CONTENT:" + fileName);
        File.SetCreationTimeUtc(filePath, creationTimeUtc);

        return filePath;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string directoryPath in _createdDirectories)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
            }
            catch (IOException)
            {
                // Teardown is best-effort: a lingering lock must not fail an otherwise passing test.
            }
            catch (UnauthorizedAccessException)
            {
                // Teardown is best-effort, see the IOException branch above.
            }
        }

        await _connection.DisposeAsync();
    }

    private string CreateUniqueDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "scannerservice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        _createdDirectories.Add(directoryPath);

        return directoryPath;
    }
}
