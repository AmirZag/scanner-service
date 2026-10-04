using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Infrastructure.Persistence;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly FakeScannerQueries _scannerQueries = new(new List<ScannerDto>
    {
        new("escl-1", "HP Color LaserJet MFP", "Escl"),
        new("wia-1", "Canon DR-C225", "Wia")
    });

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
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IScannerQueries>(_scannerQueries);
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
    public async Task GetHealth_ReturnsRunningTrueWithAssemblyVersion()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiHealthCheckDto payload = await TestApiHost.ReadJsonAsync<ApiHealthCheckDto>(response);
        Assert.True(payload.IsRunning);
        Assert.Matches(@"^\d+(\.\d+){1,3}$", payload.Version);
    }

    [Fact]
    public async Task GetDetailedHealth_AllDependenciesHealthy_Returns200Healthy()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/health/detailed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DetailedApiHealthCheckDto payload = await TestApiHost.ReadJsonAsync<DetailedApiHealthCheckDto>(response);
        Assert.True(payload.IsHealthy);
        Assert.True(payload.Dependencies["Database"]);
        Assert.True(payload.Dependencies["Scanners"]);
        Assert.NotEqual(string.Empty, payload.Version);

        // The detailed handler echoes the correlation id its response header carries.
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        string headerCorrelationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Equal(headerCorrelationId, payload.CorrelationId);
    }

    [Fact]
    public async Task GetDetailedHealth_WhenDatabaseCheckThrows_ReportsDatabaseDependencyDown()
    {
        // A connection string whose directory does not exist makes CanConnectAsync THROW (SQLite
        // cannot create the file) instead of returning false, which drives the handler's
        // catch-all branch for the database dependency.
        string unusableDatabasePath = Path.Combine(
            Path.GetTempPath(), "scanner-health-tests-" + Guid.NewGuid().ToString("N"), "scanner.db");
        await using SqliteConnection unusableConnection = new SqliteConnection("Data Source=" + unusableDatabasePath);
        HostFixture brokenDatabaseFixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddDbContext<Context>(options => options.UseSqlite(unusableConnection));
            services.AddMemoryCache();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IScannerQueries>(new FakeScannerQueries(new List<ScannerDto>
            {
                new("escl-1", "HP Color LaserJet MFP", "Escl")
            }));
        });
        try
        {
            using HttpResponseMessage response = await brokenDatabaseFixture.Client.GetAsync("/api/health/detailed");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DetailedApiHealthCheckDto payload = await TestApiHost.ReadJsonAsync<DetailedApiHealthCheckDto>(response);
            Assert.False(payload.IsHealthy);
            Assert.False(payload.Dependencies["Database"]);
            Assert.True(payload.Dependencies["Scanners"]);
        }
        finally
        {
            await brokenDatabaseFixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetDetailedHealth_WhenDatabaseDependencyIsDisposed_ReportsDatabaseDependencyDown()
    {
        // A disposed DbContext makes CanConnectAsync throw ObjectDisposedException (EF checks its
        // disposed flag before any provider work), which drives the handler's catch-all branch -
        // distinct from CanConnect swallowing a provider error and returning false.
        SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
        Context disposedContext = new Context(new DbContextOptionsBuilder<Context>().UseSqlite(connection).Options);
        disposedContext.Dispose();
        connection.Dispose();

        HostFixture disposedDatabaseFixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddMemoryCache();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IScannerQueries>(new FakeScannerQueries(new List<ScannerDto>
            {
                new("escl-1", "HP Color LaserJet MFP", "Escl")
            }));
            services.AddSingleton(disposedContext);
        });
        try
        {
            using HttpResponseMessage response = await disposedDatabaseFixture.Client.GetAsync("/api/health/detailed");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DetailedApiHealthCheckDto payload = await TestApiHost.ReadJsonAsync<DetailedApiHealthCheckDto>(response);
            Assert.False(payload.IsHealthy);
            Assert.False(payload.Dependencies["Database"]);
            Assert.True(payload.Dependencies["Scanners"]);
        }
        finally
        {
            await disposedDatabaseFixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetDetailedHealth_ScannerDependencyDown_StillReturns200_CurrentBehavior()
    {
        // KNOWN BUG AR-1: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // The handler assigns Response.StatusCode = 503 for unhealthy dependencies but then
        // returns TypedResults.Ok(result), whose execution overwrites the status back to 200 -
        // the documented 503 contract is never met. Once fixed, this must assert
        // HttpStatusCode.ServiceUnavailable (503) instead of OK.
        _scannerQueries.ThrowOnCall = true;

        using HttpResponseMessage response = await _client.GetAsync("/api/health/detailed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DetailedApiHealthCheckDto payload = await TestApiHost.ReadJsonAsync<DetailedApiHealthCheckDto>(response);
        Assert.False(payload.IsHealthy);
        Assert.False(payload.Dependencies["Scanners"]);
        Assert.True(payload.Dependencies["Database"]);
    }
}
