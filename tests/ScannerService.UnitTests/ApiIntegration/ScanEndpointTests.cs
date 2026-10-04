using System.IO.Compression;
using System.Net;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using ScannerService.Infrastructure.Services;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Exercises POST /api/scan end to end with the REAL ScanJobService (over the real SQLite
/// database and export-path validation) and a fake IScannerService standing in for the NAPS2
/// hardware stack. Joins the ScanJobStatics collection because ScanJobService tracks temp zip
/// files in static state shared across the whole test assembly.
/// </summary>
[Collection("BinState")]
public sealed class ScanEndpointTests : IAsyncLifetime
{
    private readonly FakeScannerService _scannerService = new();
    private readonly byte[] _pdfBytes = new byte[] { 1, 2, 3, 4, 5 };
    private readonly byte[] _jpgBytes = new byte[] { 9, 8, 7 };

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
            services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddScoped<IProfileRepository, ProfileRepository>();
            services.AddScoped<IExportSettingRepository, ExportSettingRepository>();
            services.AddScoped<IScanJobService, ScanJobService>();
            services.AddSingleton<IScannerService>(_scannerService);
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
    public async Task PostScan_UnknownProfile_Returns400WithNotFound()
    {
        using HttpResponseMessage response = await PostScanAsync(4242, "PDF");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Profile 4242 not found", await TestApiHost.ReadErrorAsync(response));
    }

    [Fact]
    public async Task PostScan_ProfileWithoutDevice_Returns400()
    {
        ProfileDto created = await CreateProfileAsync("""{"name": "Deviceless Profile"}""");

        using HttpResponseMessage response = await PostScanAsync(created.Id, "PDF");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Profile does not have a scanner device assigned", await TestApiHost.ReadErrorAsync(response));
    }

    [Fact]
    public async Task PostScan_SingleFileSuccess_StreamsFileBytes()
    {
        ProfileDto profile = await CreateDeviceProfileAsync();
        string outputPath = await CreateOutputFileAsync("scan-output.pdf", _pdfBytes);
        _scannerService.FilesToReturn.Add(outputPath);

        using HttpResponseMessage response = await PostScanAsync(profile.Id, "JPEG");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        string? dispositionFileName = response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(dispositionFileName);
        Assert.Equal("scan-output.pdf", dispositionFileName);
        byte[] bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(_pdfBytes, bodyBytes);

        Assert.NotNull(_scannerService.LastConfiguration);
        Assert.Equal("device-abc", _scannerService.LastConfiguration.DeviceId);
        Assert.Equal("JPEG", _scannerService.LastConfiguration.Format);
        Assert.Equal(ExportPathForJson, _scannerService.LastConfiguration.ExportPath);
    }

    [Fact]
    public async Task PostScan_MultipleFiles_StreamsZipArchive()
    {
        ProfileDto profile = await CreateDeviceProfileAsync();
        string first = await CreateOutputFileAsync("page-1.jpg", _jpgBytes);
        string second = await CreateOutputFileAsync("page-2.jpg", new byte[] { 6, 5, 4 });
        _scannerService.FilesToReturn.Add(first);
        _scannerService.FilesToReturn.Add(second);

        using HttpResponseMessage response = await PostScanAsync(profile.Id, "PDF");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        string? dispositionFileName = response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(dispositionFileName);
        Assert.StartsWith("scanned_documents_", dispositionFileName);
        Assert.EndsWith(".zip", dispositionFileName, StringComparison.Ordinal);

        byte[] zipBytes = await response.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, entry => entry.Name == "page-1.jpg");
        Assert.Contains(archive.Entries, entry => entry.Name == "page-2.jpg");
    }

    [Fact]
    public async Task PostScan_InvalidProfileId_Returns400ValidationProblem()
    {
        using HttpResponseMessage response = await PostScanAsync(0, "PDF");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("ProfileId", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("ProfileId must be greater than 0", messages);
    }

    [Fact]
    public async Task PostScan_ScannerFailureResult_Returns400WithScannerError()
    {
        ProfileDto profile = await CreateDeviceProfileAsync();
        _scannerService.FailWithResult = true;

        using HttpResponseMessage response = await PostScanAsync(profile.Id, "PDF");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("scanner offline", await TestApiHost.ReadErrorAsync(response));
    }

    [Fact]
    public async Task PostScan_ScannerException_Returns400WithExceptionMessage()
    {
        ProfileDto profile = await CreateDeviceProfileAsync();
        _scannerService.ThrowOnExecute = true;

        using HttpResponseMessage response = await PostScanAsync(profile.Id, "PDF");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("driver exploded", await TestApiHost.ReadErrorAsync(response));
    }

    private string ExportPathForJson => _exportDirectory.Replace('\\', '/');

    private async Task<HttpResponseMessage> PostScanAsync(int profileId, string format)
    {
        string json = $"{{\"profileId\": {profileId}, \"exportPath\": \"{ExportPathForJson}\", \"format\": \"{format}\"}}";
        return await TestApiHost.PostJsonAsync(_client, "/api/scan", json);
    }

    private async Task<ProfileDto> CreateDeviceProfileAsync()
    {
        return await CreateProfileAsync("""{"name": "Scan Profile", "deviceId": "device-abc"}""");
    }

    private async Task<ProfileDto> CreateProfileAsync(string json)
    {
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(_client, "/api/profiles", json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await TestApiHost.ReadJsonAsync<ProfileDto>(response);
    }

    private async Task<string> CreateOutputFileAsync(string fileName, byte[] content)
    {
        string path = Path.Combine(_exportDirectory, fileName);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
