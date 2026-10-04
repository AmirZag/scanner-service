using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

public sealed class ScannerEndpointTests : IAsyncLifetime
{
    private readonly FakeScannerQueries _scannerQueries = new(new List<ScannerDto>
    {
        new("escl-1", "HP Color LaserJet MFP", "Escl"),
        new("twain-1", "Epson DS-530", "Twain")
    });

    private readonly FakeScannerListCache _scannerListCache = new();

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        HostFixture fixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddSingleton<IScannerQueries>(_scannerQueries);
            services.AddSingleton<IScannerListCache>(_scannerListCache);
        });

        _app = fixture.App;
        _client = fixture.Client;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            _client.Dispose();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetScanners_ReturnsDevicesFromQueryService()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/scanners");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<ScannerDto> devices = await TestApiHost.ReadJsonAsync<List<ScannerDto>>(response);
        Assert.Equal(2, devices.Count);
        Assert.Equal("escl-1", devices[0].Id);
        Assert.Equal("HP Color LaserJet MFP", devices[0].Name);
        Assert.Equal("Escl", devices[0].Driver);
        Assert.Equal("twain-1", devices[1].Id);
        Assert.Equal("Epson DS-530", devices[1].Name);
        Assert.Equal("Twain", devices[1].Driver);
        Assert.Equal(1, _scannerQueries.CallCount);
    }

    [Fact]
    public async Task PostScannersRefresh_ClearsCacheSoNextGetReenumerates()
    {
        using HttpResponseMessage first = await _client.GetAsync("/api/scanners");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, _scannerQueries.CallCount);

        using HttpResponseMessage refresh = await _client.PostAsync("/api/scanners/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        string refreshBody = await refresh.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(refreshBody);
        Assert.True(document.RootElement.TryGetProperty("message", out JsonElement message));
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));

        // The cache-clear endpoint does not enumerate itself; the NEXT list request does.
        using HttpResponseMessage second = await _client.GetAsync("/api/scanners");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        List<ScannerDto> devices = await TestApiHost.ReadJsonAsync<List<ScannerDto>>(second);
        Assert.Equal(2, devices.Count);
        Assert.Equal(2, _scannerQueries.CallCount);
        Assert.Equal(1, _scannerListCache.ClearCallCount);
    }
}
