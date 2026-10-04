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
    public async Task Invoke_RotatingForwardedForHeaders_NeverReachesLimit_CurrentBehavior()
    {
        // KNOWN BUG S-1: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // The limiter keys on the client-controlled X-Forwarded-For header (no proxy exists in
        // front of Kestrel), so rotating values gives every request a fresh bucket and the
        // 100-requests-per-minute limit never triggers. Once fixed (keying on RemoteIpAddress
        // only), the 101st request must return 429 TooManyRequests.
        for (int requestIndex = 0; requestIndex <= ApplicationConstants.RateLimit.DefaultMaxRequests; requestIndex++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PingPath);
            request.Headers.Add("X-Forwarded-For", "203.0.113." + requestIndex.ToString(CultureInfo.InvariantCulture));

            using HttpResponseMessage response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
