using System.Net;
using System.Threading;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Validators;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Exercises the /api/settings endpoints against the REAL LocalSettingsStore and a REAL (stopped)
/// WebApiHostService wrapped in the real ApiHostControl, so the restart trigger surfaces through
/// the actual RestartRequested event. Joins the BinState collection because the sidecar
/// (appsettings.local.json) lives in the shared test bin directory.
/// </summary>
[Collection("BinState")]
public sealed class SettingsEndpointTests : IAsyncLifetime
{
    private const string ValidSettingsJson = """
        {
            "statusCheckInterval": 7000,
            "httpTimeout": 2500,
            "startupDelay": 3000,
            "driverTimeoutMs": 20000,
            "esclSearchTimeoutMs": 9000,
            "esclSearchMarginMs": 2500,
            "driverCooldownMs": 70000,
            "driverCooldownMaxMs": 700000,
            "scanQueueTimeoutMs": 6000,
            "scanOverallTimeoutMs": 700000,
            "scanNoProgressTimeoutMs": 130000,
            "shutdownTimeoutMs": 6000,
            "scannersRequestTimeoutSeconds": 30,
            "scanRequestTimeoutSeconds": 760,
            "esclManualDevices": []
        }
        """;

    private const string OutOfRangeSettingsJson = """
        {
            "statusCheckInterval": 5,
            "httpTimeout": 2500,
            "startupDelay": 3000,
            "driverTimeoutMs": 20000,
            "esclSearchTimeoutMs": 9000,
            "esclSearchMarginMs": 2500,
            "driverCooldownMs": 70000,
            "driverCooldownMaxMs": 700000,
            "scanQueueTimeoutMs": 6000,
            "scanOverallTimeoutMs": 700000,
            "scanNoProgressTimeoutMs": 130000,
            "shutdownTimeoutMs": 6000,
            "scannersRequestTimeoutSeconds": 30,
            "scanRequestTimeoutSeconds": 760,
            "esclManualDevices": []
        }
        """;

    private const string NullDevicesSettingsJson = """
        {
            "statusCheckInterval": 7000,
            "httpTimeout": 2500,
            "startupDelay": 3000,
            "driverTimeoutMs": 20000,
            "esclSearchTimeoutMs": 9000,
            "esclSearchMarginMs": 2500,
            "driverCooldownMs": 70000,
            "driverCooldownMaxMs": 700000,
            "scanQueueTimeoutMs": 6000,
            "scanOverallTimeoutMs": 700000,
            "scanNoProgressTimeoutMs": 130000,
            "shutdownTimeoutMs": 6000,
            "scannersRequestTimeoutSeconds": 30,
            "scanRequestTimeoutSeconds": 760,
            "esclManualDevices": null
        }
        """;

    private const string MissingHttpTimeoutSettingsJson = """
        {
            "statusCheckInterval": 7000,
            "startupDelay": 3000,
            "driverTimeoutMs": 20000,
            "esclSearchTimeoutMs": 9000,
            "esclSearchMarginMs": 2500,
            "driverCooldownMs": 70000,
            "driverCooldownMaxMs": 700000,
            "scanQueueTimeoutMs": 6000,
            "scanOverallTimeoutMs": 700000,
            "scanNoProgressTimeoutMs": 130000,
            "shutdownTimeoutMs": 6000,
            "scannersRequestTimeoutSeconds": 30,
            "scanRequestTimeoutSeconds": 760,
            "esclManualDevices": []
        }
        """;

    private ScannerServiceConfiguration _baseConfig = null!;
    private LocalSettingsStore _store = null!;
    private WebApiHostService _hostService = null!;
    private HostFixture _fixture = null!;
    private HttpClient _client = null!;
    private int _restartCount;

    public async Task InitializeAsync()
    {
        DeleteSidecarFiles();

        // A deliberately non-default base HttpTimeout lets tests PROVE whether a value came from
        // the base snapshot (3000) or from the settings DTO defaults (2000).
        _baseConfig = new ScannerServiceConfiguration { HttpTimeout = 3000 };
        _store = new LocalSettingsStore(_baseConfig);
        _hostService = new WebApiHostService(_baseConfig, _store);
        _hostService.RestartRequested += (_, _) => Interlocked.Increment(ref _restartCount);

        _fixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(_store);
            services.AddSingleton<IApiHostControl>(new ApiHostControl(_hostService));
        });

        _client = _fixture.Client;
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        if (_hostService is not null)
        {
            // Stopped-state host: Dispose only runs the bounded stop (a no-op) and flushes logging.
            _hostService.Dispose();
        }

        DeleteSidecarFiles();
    }

    [Fact]
    public async Task GetSettings_NoSidecar_ReturnsBaseConfiguration()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(response);
        Assert.Equal(5000, settings.StatusCheckInterval);
        Assert.Equal(3000, settings.HttpTimeout);
        Assert.Equal(15000, settings.DriverTimeoutMs);
        Assert.Equal(600000, settings.ScanOverallTimeoutMs);
        Assert.Empty(settings.EsclManualDevices);
        Assert.False(_store.SidecarExists);
    }

    [Fact]
    public async Task GetSettings_SparseSidecarOverride_MergesOntoBase()
    {
        // Sparse sidecar shape: only the first field (StatusCheckInterval) is overridden.
        var overrides = new ScannerSettingsOverridesDto(
            9000, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.True((await _store.WriteOverrides(overrides)).IsSuccess);

        using HttpResponseMessage response = await _client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(response);
        Assert.Equal(9000, settings.StatusCheckInterval);
        Assert.Equal(3000, settings.HttpTimeout);
    }

    [Fact]
    public async Task GetSettings_InvalidSidecarValues_FallsBackToBase()
    {
        var overrides = new ScannerSettingsOverridesDto(
            5, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.True((await _store.WriteOverrides(overrides)).IsSuccess);

        using HttpResponseMessage response = await _client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(response);
        Assert.Equal(5000, settings.StatusCheckInterval);
        Assert.Equal(3000, settings.HttpTimeout);
    }

    [Fact]
    public async Task GetSettings_CorruptSidecar_ServesBaseAndQuarantinesFile()
    {
        string sidecarPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName);
        await File.WriteAllTextAsync(sidecarPath, "{ this is not valid json");

        using HttpResponseMessage response = await _client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(response);
        Assert.Equal(5000, settings.StatusCheckInterval);
        Assert.Equal(3000, settings.HttpTimeout);
        Assert.False(_store.SidecarExists);
        Assert.True(File.Exists(sidecarPath + ".invalid"));
    }

    [Fact]
    public async Task PutSettings_ValidFullBody_PersistsSidecarAndRaisesRestart()
    {
        using HttpResponseMessage putResponse = await TestApiHost.PutJsonAsync(_client, "/api/settings", ValidSettingsJson);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        SettingsUpdateResponseDto payload = await TestApiHost.ReadJsonAsync<SettingsUpdateResponseDto>(putResponse);
        Assert.True(payload.Restarting);
        Assert.False(string.IsNullOrWhiteSpace(payload.Warning));
        Assert.True(_store.SidecarExists);

        // The restart fires from Response.OnCompleted after the response has been sent.
        await TestApiHost.WaitUntilAsync(() => Volatile.Read(ref _restartCount) >= 1);
        Assert.Equal(1, Volatile.Read(ref _restartCount));

        using HttpResponseMessage getResponse = await _client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(getResponse);
        Assert.Equal(7000, settings.StatusCheckInterval);
        Assert.Equal(2500, settings.HttpTimeout);
    }

    [Fact]
    public async Task PutSettings_MissingNumericField_SubstitutesDtoDefault()
    {
        // Pins the actual full-replacement-with-defaults semantics (audit doc-contract gap C-53:
        // ScannerSettingsValidator's comment claims "missing numeric -> 0 -> rejected", but the
        // DTO's property initializers silently substitute defaults and the base appsettings.json
        // value is lost). httpTimeout is omitted, so the DTO default (2000) replaces the base
        // value (3000) instead of being rejected or preserved.
        using HttpResponseMessage putResponse = await TestApiHost.PutJsonAsync(
            _client, "/api/settings", MissingHttpTimeoutSettingsJson);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        SettingsUpdateResponseDto payload = await TestApiHost.ReadJsonAsync<SettingsUpdateResponseDto>(putResponse);
        Assert.True(payload.Restarting);

        using HttpResponseMessage getResponse = await _client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        ScannerSettingsDto settings = await TestApiHost.ReadJsonAsync<ScannerSettingsDto>(getResponse);
        Assert.Equal(2000, settings.HttpTimeout);
        Assert.Equal(7000, settings.StatusCheckInterval);
    }

    [Fact]
    public async Task PutSettings_OutOfRangeValue_Returns400WithoutSideEffect()
    {
        using HttpResponseMessage response = await TestApiHost.PutJsonAsync(_client, "/api/settings", OutOfRangeSettingsJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("ScannerService", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("StatusCheckInterval must be between 1000ms and 300000ms", messages[0]);
        Assert.False(_store.SidecarExists);

        await TestApiHost.WaitShortDelayAsync();
        Assert.Equal(0, Volatile.Read(ref _restartCount));
    }

    [Fact]
    public async Task PutSettings_WhenSidecarFileIsLocked_Returns500WithoutSideEffects()
    {
        string sidecarPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName);
        await File.WriteAllTextAsync(sidecarPath, "{}");
        HttpStatusCode statusCode;
        string body;
        using (FileStream lockStream = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using HttpResponseMessage response = await TestApiHost.PutJsonAsync(_client, "/api/settings", ValidSettingsJson);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync();
        }

        Assert.Equal(HttpStatusCode.InternalServerError, statusCode);
        Assert.Contains("Failed to persist settings", body, StringComparison.Ordinal);
        Assert.True(_store.SidecarExists);
        Assert.Equal("{}", await File.ReadAllTextAsync(sidecarPath));

        await TestApiHost.WaitShortDelayAsync();
        Assert.Equal(0, Volatile.Read(ref _restartCount));
    }

    [Fact]
    public async Task DeleteOverrides_WhenSidecarFileIsLocked_Returns500WithoutRestart()
    {
        string sidecarPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName);
        await File.WriteAllTextAsync(sidecarPath, "{}");
        using FileStream lockStream = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            using HttpResponseMessage response = await _client.DeleteAsync("/api/settings/overrides");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Failed to reset settings", body, StringComparison.Ordinal);
            Assert.True(_store.SidecarExists);

            await TestApiHost.WaitShortDelayAsync();
            Assert.Equal(0, Volatile.Read(ref _restartCount));
        }
        finally
        {
            await lockStream.DisposeAsync();
        }
    }

    [Fact]
    public async Task PutSettings_NullEsclManualDevices_ThrowsNullReference_CurrentBehavior()
    {
        // KNOWN BUG C-1b: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // ScannerSettingsValidator chains .Must(...) after .NotNull(...) under the default
        // Continue cascade, so a null esclManualDevices still reaches devices.Count and throws
        // NullReferenceException inside the validator (surfacing as a server exception instead of
        // a 400). Once fixed, the PUT must return a 400 validation problem instead of throwing.
        await Assert.ThrowsAsync<NullReferenceException>(
            () => TestApiHost.PutJsonAsync(_client, "/api/settings", NullDevicesSettingsJson));
    }

    [Fact]
    public async Task PutSettings_EmptyDeviceAddress_Returns400()
    {
        string json = ValidSettingsJson.Replace(
            "\"esclManualDevices\": []",
            "\"esclManualDevices\": [ { \"name\": \"HP\", \"address\": \"\" } ]",
            StringComparison.Ordinal);

        using HttpResponseMessage response = await TestApiHost.PutJsonAsync(_client, "/api/settings", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("EsclManualDevices", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("Each EsclManualDevices entry needs a non-empty Address", messages[0]);
    }

    [Fact]
    public async Task DeleteOverrides_WithoutSidecar_ReturnsRestartingFalseWithoutEvent()
    {
        using HttpResponseMessage response = await _client.DeleteAsync("/api/settings/overrides");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SettingsUpdateResponseDto payload = await TestApiHost.ReadJsonAsync<SettingsUpdateResponseDto>(response);
        Assert.False(payload.Restarting);
        Assert.False(_store.SidecarExists);

        await TestApiHost.WaitShortDelayAsync();
        Assert.Equal(0, Volatile.Read(ref _restartCount));
    }

    [Fact]
    public async Task PutThenDelete_RemovesSidecarAndRaisesRestartTwice()
    {
        using HttpResponseMessage putResponse = await TestApiHost.PutJsonAsync(_client, "/api/settings", ValidSettingsJson);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        await TestApiHost.WaitUntilAsync(() => Volatile.Read(ref _restartCount) >= 1);

        using HttpResponseMessage deleteResponse = await _client.DeleteAsync("/api/settings/overrides");

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        SettingsUpdateResponseDto payload = await TestApiHost.ReadJsonAsync<SettingsUpdateResponseDto>(deleteResponse);
        Assert.True(payload.Restarting);
        Assert.False(_store.SidecarExists);

        await TestApiHost.WaitUntilAsync(() => Volatile.Read(ref _restartCount) >= 2);
        Assert.Equal(2, Volatile.Read(ref _restartCount));
    }

    private static void DeleteSidecarFiles()
    {
        // The sidecar lives in the shared test bin directory (AppContext.BaseDirectory), so every
        // BinState test must remove it - and its quarantine twin - in setup AND teardown.
        // File.Delete on a missing path is a no-op.
        string sidecarPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsStore.FileName);
        File.Delete(sidecarPath);
        File.Delete(sidecarPath + ".invalid");
    }
}
