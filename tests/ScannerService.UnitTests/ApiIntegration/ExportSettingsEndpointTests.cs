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
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

public sealed class ExportSettingsEndpointTests : IAsyncLifetime
{
    private SqliteConnection _sqliteConnection = null!;
    private HostFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();
        _fixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddDbContext<Context>(options => options.UseSqlite(_sqliteConnection));
            services.AddMemoryCache();
            services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddScoped<IExportSettingRepository, ExportSettingRepository>();
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
    }

    [Fact]
    public async Task GetExportSettings_SeededDatabase_ReturnsDefaults()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/export-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ExportSettingDto settings = await TestApiHost.ReadJsonAsync<ExportSettingDto>(response);
        Assert.Equal("PDF", settings.Format);
        Assert.Equal(string.Empty, settings.ExportPath);
        Assert.Equal("scan_{datetime}", settings.FileName);
    }

    [Fact]
    public async Task PutExportSettings_ValidBody_PersistsEveryField()
    {
        string exportPath = TestApiHost.CreateTempDirectory();
        try
        {
            string exportPathJson = exportPath.Replace('\\', '/');
            string json = $"{{\"format\": \"TIFF\", \"exportPath\": \"{exportPathJson}\", \"fileName\": \"doc_{{datetime}}\"}}";

            using HttpResponseMessage putResponse = await TestApiHost.PutJsonAsync(_client, "/api/export-settings", json);
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

            using HttpResponseMessage getResponse = await _client.GetAsync("/api/export-settings");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            ExportSettingDto settings = await TestApiHost.ReadJsonAsync<ExportSettingDto>(getResponse);
            Assert.Equal("TIFF", settings.Format);
            Assert.Equal(exportPathJson, settings.ExportPath);
            Assert.Equal("doc_{datetime}", settings.FileName);
        }
        finally
        {
            TestApiHost.DeleteDirectoryIfExists(exportPath);
        }
    }

    [Fact]
    public async Task PutExportSettings_UnknownFormat_Returns400WithFormatKey()
    {
        using HttpResponseMessage response = await TestApiHost.PutJsonAsync(
            _client,
            "/api/export-settings",
            """{"format": "GIF", "exportPath": "", "fileName": "scan_{datetime}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("Format", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("Format must be one of: PDF, JPEG, PNG, TIFF, MultiPageTIFF", messages);
    }

    [Fact]
    public async Task PutExportSettings_RelativeExportPath_Returns400WithExportPathKey()
    {
        using HttpResponseMessage response = await TestApiHost.PutJsonAsync(
            _client,
            "/api/export-settings",
            """{"format": "PDF", "exportPath": "scans/outbox", "fileName": "scan_{datetime}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("ExportPath", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("ExportPath must be a valid absolute path or empty", messages);
    }

    [Fact]
    public async Task PutExportSettings_NullFileName_Returns400WithFileNameKey()
    {
        // FIXED (Phase 2 Batch 4, audit C-1a): the validator's predicates are null-tolerant, so a
        // null fileName surfaces as a 400 validation problem instead of a server exception.
        using HttpResponseMessage response = await TestApiHost.PutJsonAsync(
            _client,
            "/api/export-settings",
            """{"format": "PDF", "exportPath": "", "fileName": null}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("FileName", out string[]? messages));
        Assert.NotNull(messages);
    }
}
