using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

public sealed class RecentScansEndpointTests : IAsyncLifetime
{
    private SqliteConnection _sqliteConnection = null!;
    private HostFixture _fixture = null!;
    private HttpClient _client = null!;
    private string _exportDirectory = null!;

    public async Task InitializeAsync()
    {
        _exportDirectory = TestApiHost.CreateTempDirectory();
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();
        _fixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddDbContext<Context>(options => options.UseSqlite(_sqliteConnection));
            services.AddMemoryCache();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddScoped<IRecentScansService, RecentScansService>();
        });

        await using AsyncServiceScope scope = _fixture.App.Services.CreateAsyncScope();
        Context context = scope.ServiceProvider.GetRequiredService<Context>();
        await context.Database.EnsureCreatedAsync();

        _client = _fixture.Client;
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        if (_sqliteConnection is not null)
        {
            await _sqliteConnection.DisposeAsync();
        }

        TestApiHost.DeleteDirectoryIfExists(_exportDirectory);
    }

    [Fact]
    public async Task GetRecentScans_CountZero_Returns404()
    {
        // The route constraint is {count:int:min(1):max(100)}; a violating value never matches
        // the route, so the endpoint answers 404 (not 400).
        using HttpResponseMessage response = await _client.GetAsync("/api/recent-scans/0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRecentScans_CountAboveMax_Returns404()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/recent-scans/101");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRecentScans_CountOne_ReturnsOnlyNewestGroup()
    {
        await SeedTwoScanGroupsAsync();
        await SeedExportPathAsync(_exportDirectory);

        using HttpResponseMessage response = await _client.GetAsync("/api/recent-scans/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecentScansResponseDto payload = await TestApiHost.ReadJsonAsync<RecentScansResponseDto>(response);
        Assert.Equal(2, payload.TotalGroups);
        Assert.Equal(1, payload.RequestedCount);
        ScanGroupDto only = Assert.Single(payload.Groups);
        Assert.Equal("manual", only.ScanId);
    }

    [Fact]
    public async Task GetRecentScans_WithSeededFiles_GroupsNewestFirst()
    {
        await SeedTwoScanGroupsAsync();
        await SeedExportPathAsync(_exportDirectory);

        using HttpResponseMessage response = await _client.GetAsync("/api/recent-scans/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecentScansResponseDto payload = await TestApiHost.ReadJsonAsync<RecentScansResponseDto>(response);
        Assert.Equal(2, payload.TotalGroups);
        Assert.Equal(5, payload.RequestedCount);
        Assert.Equal(2, payload.Groups.Count);

        ScanGroupDto newest = payload.Groups[0];
        Assert.Equal("manual", newest.ScanId);
        Assert.Equal("pdf", newest.Format);
        Assert.Equal(1, newest.FileCount);
        Assert.Equal("application/pdf", newest.Files[0].ContentType);
        Assert.Equal("manual.pdf", newest.Files[0].Filename);
        Assert.True(newest.Files[0].SizeBytes > 0);

        ScanGroupDto grouped = payload.Groups[1];
        Assert.Equal("report_20261003_101010", grouped.ScanId);
        Assert.Equal("jpg", grouped.Format);
        Assert.Equal(2, grouped.FileCount);
        Assert.All(grouped.Files, file => Assert.Equal("image/jpeg", file.ContentType));
        Assert.True(newest.Timestamp > grouped.Timestamp, "groups must be ordered newest first");
    }

    [Fact]
    public async Task GetRecentScans_MissingExportDirectory_ReturnsEmptyGroups()
    {
        string missingDirectory = Path.Combine(_exportDirectory, "missing-" + Guid.NewGuid().ToString("N"));
        await SeedExportPathAsync(missingDirectory);

        using HttpResponseMessage response = await _client.GetAsync("/api/recent-scans/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecentScansResponseDto payload = await TestApiHost.ReadJsonAsync<RecentScansResponseDto>(response);
        Assert.Equal(0, payload.TotalGroups);
        Assert.Empty(payload.Groups);
    }

    /// <summary>
    /// Seeds three files that group into exactly two scans: a two-page jpg pair sharing the
    /// scanner's base_yyyyMMdd_HHmmss token (group "report_20261003_101010") and a single newer
    /// pdf (manual.pdf -> group "manual").
    /// </summary>
    private async Task SeedTwoScanGroupsAsync()
    {
        string reportPage1 = Path.Combine(_exportDirectory, "report_20261003_101010_1.jpg");
        string reportPage2 = Path.Combine(_exportDirectory, "report_20261003_101010_2.jpg");
        string manual = Path.Combine(_exportDirectory, "manual.pdf");
        await File.WriteAllTextAsync(reportPage1, "page-one");
        await File.WriteAllTextAsync(reportPage2, "page-two");
        await File.WriteAllTextAsync(manual, "manual-document");

        // CreationTimeUtc drives both the group timestamp and the newest-first ordering; pin
        // distinct values so the order cannot depend on file-write timing.
        DateTime older = DateTime.UtcNow.AddHours(-2);
        DateTime newer = DateTime.UtcNow.AddHours(-1);
        File.SetCreationTimeUtc(reportPage1, older.AddSeconds(-1));
        File.SetCreationTimeUtc(reportPage2, older);
        File.SetCreationTimeUtc(manual, newer);
    }

    private async Task SeedExportPathAsync(string exportPath)
    {
        await using AsyncServiceScope scope = _fixture.App.Services.CreateAsyncScope();
        Context context = scope.ServiceProvider.GetRequiredService<Context>();
        ExportSetting setting = await context.ExportSettings.SingleAsync();
        setting.ExportPath = exportPath;
        await context.SaveChangesAsync();
    }
}
