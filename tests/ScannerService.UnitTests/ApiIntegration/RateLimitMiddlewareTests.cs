using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ScannerService.Domain.Common;
using ScannerService.TrayApp.Middleware;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Drives the REAL RateLimitMiddleware (with its real options from ApplicationConstants) through
/// a standalone TestServer mini-app. Each test class instance builds a fresh app, and the
/// middleware state (counters, per-IP locks) is per-instance, so tests never leak limits into
/// each other.
/// </summary>
public sealed class RateLimitMiddlewareTests : IAsyncLifetime
{
    private const string PingPath = "/ping";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMemoryCache();

        WebApplication app = builder.Build();
        app.UseMiddleware<RateLimitMiddleware>(new RateLimitOptions
        {
            MaxRequests = ApplicationConstants.RateLimit.DefaultMaxRequests,
            Window = TimeSpan.FromMinutes(ApplicationConstants.RateLimit.DefaultWindowMinutes)
        });
        app.MapGet(PingPath, () => "pong");
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
    public async Task Invoke_WithinMaxRequests_AllSucceedThenNextIsLimited()
    {
        for (int requestIndex = 0; requestIndex < ApplicationConstants.RateLimit.DefaultMaxRequests; requestIndex++)
        {
            using HttpResponseMessage response = await _client.GetAsync(PingPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using HttpResponseMessage limited = await _client.GetAsync(PingPath);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        string body = await limited.Content.ReadAsStringAsync();
        Assert.Contains("Rate limit exceeded", body);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retryAfterValues));
        string retryAfter = Assert.Single(retryAfterValues!);
        double retryAfterSeconds = double.Parse(retryAfter, CultureInfo.InvariantCulture);
        Assert.True(retryAfterSeconds > 0, "Retry-After must be positive while the window is still active");
    }

    [Fact]
    public async Task Invoke_RotatingForwardedForHeaders_AreIgnoredAndLimitStillApplies()
    {
        // FIXED (Phase 2 Batch 4, audit S-1): the limiter keys on the connection's remote address
        // only - the client-controlled X-Forwarded-For header is ignored, so rotating values no
        // longer buys fresh buckets and the 101st request hits the limit.
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int requestIndex = 0; requestIndex <= ApplicationConstants.RateLimit.DefaultMaxRequests; requestIndex++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PingPath);
            request.Headers.Add("X-Forwarded-For", "203.0.113." + requestIndex.ToString(CultureInfo.InvariantCulture));

            using HttpResponseMessage response = await _client.SendAsync(request);
            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }
}
