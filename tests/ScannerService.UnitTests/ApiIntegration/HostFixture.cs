using Microsoft.AspNetCore.Builder;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Owns a started TestServer-backed <see cref="WebApplication"/> and its test client. Disposal
/// order matters: the client first (it references the server), then the app (which tears down
/// the DI container and the TestServer itself).
/// </summary>
internal sealed class HostFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HostFixture(WebApplication app, HttpClient client)
    {
        _app = app;
        _client = client;
    }

    public WebApplication App => _app;

    public HttpClient Client => _client;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
