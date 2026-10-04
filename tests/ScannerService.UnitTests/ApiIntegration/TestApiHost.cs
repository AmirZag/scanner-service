using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ScannerService.Domain.Common;
using ScannerService.TrayApp.Configurations;
using ScannerService.TrayApp.Middleware;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Builds a TestServer-hosted API app with the real production pipeline pieces the endpoints
/// need (request-timeout policies, correlation-id middleware, the real
/// <see cref="EndpointConfigurationExtensions.ConfigureAllEndpoints"/> map) while each test
/// class supplies its own DI graph. The app is never started on Kestrel and never binds a port.
/// </summary>
internal static class TestApiHost
{
    public static async Task<HostFixture> CreateAsync(Action<IServiceCollection> configureServices)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // The endpoint map attaches named request-timeout policies; register generous bounds so
        // tests can never trip the 408 backstop.
        builder.Services.AddRequestTimeouts(options =>
        {
            options.AddPolicy(ApplicationConstants.RequestTimeoutPolicies.Scanners, TimeSpan.FromMinutes(10));
            options.AddPolicy(ApplicationConstants.RequestTimeoutPolicies.Scan, TimeSpan.FromMinutes(30));
        });
        configureServices(builder.Services);
        DefaultEndpointDependencies.Register(builder.Services);

        WebApplication app = builder.Build();
        app.UseRequestTimeouts();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.ConfigureAllEndpoints();
        await app.StartAsync();

        HttpClient client = app.GetTestClient();
        return new HostFixture(app, client);
    }

    public static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(url, content);
    }

    public static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string url, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PutAsync(url, content);
    }

    public static async Task<HttpResponseMessage> PatchJsonAsync(HttpClient client, string url, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PatchAsync(url, content);
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        T? payload = JsonSerializer.Deserialize<T>(body, ApiJson.Web);
        Assert.NotNull(payload);
        return payload;
    }

    /// <summary>Reads the flat error body shape used by failure results: { "error": "..." }.</summary>
    public static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetString()!;
    }

    public static async Task<Dictionary<string, string[]>> ReadValidationProblemsAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        Dictionary<string, string[]>? problems = JsonSerializer.Deserialize<Dictionary<string, string[]>>(errors.GetRawText(), ApiJson.Web);
        Assert.NotNull(problems);
        return problems;
    }

    /// <summary>
    /// Bounded poll for a background side effect (the endpoints register work on
    /// Response.OnCompleted, which fires after the response has been sent). Waits at most ~2s.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Short grace window used before asserting the ABSENCE of a background side effect (a late
    /// Response.OnCompleted callback would otherwise be missed). Documented bounded async wait.
    /// </summary>
    public static async Task WaitShortDelayAsync()
    {
        await Task.Delay(250);
    }

    /// <summary>Creates a unique writable directory under the user temp path.</summary>
    public static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scanner-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectoryIfExists(string? directory)
    {
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
