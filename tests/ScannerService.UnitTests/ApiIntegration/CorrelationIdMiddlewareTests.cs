using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using ScannerService.TrayApp.Middleware;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Drives the REAL CorrelationIdMiddleware through a standalone TestServer mini-app that carries
/// only this middleware and one ping endpoint.
/// </summary>
public sealed class CorrelationIdMiddlewareTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        WebApplication app = builder.Build();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.MapGet("/ping", () => "pong");
        await app.StartAsync();

        _app = app;
        _client = app.GetTestClient();
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
    public async Task Invoke_WithoutRequestHeader_GeneratesGuidCorrelationId()
    {
        using HttpResponseMessage response = await _client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        string correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));

        // Generated ids use Guid "N" format: 32 hexadecimal characters, no dashes.
        Assert.True(Guid.TryParse(correlationId, out Guid _));
        Assert.Equal(32, correlationId.Length);
    }

    [Fact]
    public async Task Invoke_WithRequestHeader_EchoesSuppliedValue()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
        request.Headers.Add("X-Correlation-ID", "trace-abc-123");

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        string correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Equal("trace-abc-123", correlationId);
    }

    [Fact]
    public async Task Invoke_ResponseCarriesHeaderAlongsideBody()
    {
        // The header is part of the response metadata, so it is available before the body is read.
        using HttpResponseMessage response = await _client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("pong", body);
    }
}
