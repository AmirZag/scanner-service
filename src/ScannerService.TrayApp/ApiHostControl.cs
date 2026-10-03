namespace ScannerService.TrayApp;

/// <summary>
/// Read-only API host facts plus the restart trigger, resolved by the settings endpoints.
/// The wrapper is deliberately NOT IDisposable: the host's DI container disposes resolved
/// services during teardown, and disposing the owner from inside its own container shutdown
/// would re-enter the bounded stop and flush the shared Serilog logger mid-restart.
/// </summary>
public interface IApiHostControl
{
    /// <summary>Port the host actually bound (after the port-fallback search).</summary>
    int ActualPort { get; }

    /// <summary>Port configured in appsettings.json before any fallback.</summary>
    int ConfiguredPort { get; }

    /// <summary>Host string for locally-constructed URLs (mirrors WebApiHostService.LocalUrlHost).</summary>
    string BindHost { get; }

    /// <summary>Requests an in-process host restart so persisted settings take effect.</summary>
    void RequestRestart();
}

internal sealed class ApiHostControl : IApiHostControl
{
    private readonly WebApiHostService _host;

    public ApiHostControl(WebApiHostService host)
    {
        _host = host;
    }

    public int ActualPort => _host.ActualPort;

    public int ConfiguredPort => _host.ConfiguredPort;

    public string BindHost => _host.LocalUrlHost;

    public void RequestRestart()
    {
        _host.RequestRestart();
    }
}
