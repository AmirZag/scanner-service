using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class SettingsEndToEndTests : IDisposable
{
    public SettingsEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task GetSettings_WithoutSidecar_ReturnsConfiguredBaseValues()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.SettingsRelativeUrl);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1000, root.GetProperty("statusCheckInterval").GetInt32());
            Assert.Equal(3000, root.GetProperty("driverTimeoutMs").GetInt32());
            Assert.Equal(30000, root.GetProperty("scanOverallTimeoutMs").GetInt32());
            Assert.Equal(0, root.GetProperty("esclManualDevices").GetArrayLength());
            Assert.False(File.Exists(EndToEndBinState.SidecarPath));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PutSettings_WithValidFullBody_PersistsSidecarRaisesRestartEventAndKeepsHostHealthy()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            int restartCount = 0;
            host.RestartRequested += (sender, args) => Interlocked.Increment(ref restartCount);

            using HttpResponseMessage response = await client.PutAsync(EndToEndHostFactory.SettingsRelativeUrl, EndToEndHostFactory.CreateJsonContent(CreateValidSettingsBody(2000)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (JsonDocument responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                Assert.True(responseDocument.RootElement.GetProperty("restarting").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(responseDocument.RootElement.GetProperty("warning").GetString()));
            }

            // The restart raise is registered post-response (Response.OnCompleted), so it can land
            // just after the reply; poll briefly for it instead of assuming a strict ordering.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref restartCount) == 0 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Equal(1, Volatile.Read(ref restartCount));

            // Nobody consumes the event in tests (TrayApp would): the host must keep serving.
            using (HttpResponseMessage healthResponse = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl))
            {
                Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            }

            Assert.True(File.Exists(EndToEndBinState.SidecarPath));
            using (HttpResponseMessage getResponse = await client.GetAsync(EndToEndHostFactory.SettingsRelativeUrl))
            {
                using JsonDocument getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                Assert.Equal(2000, getDocument.RootElement.GetProperty("statusCheckInterval").GetInt32());
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PutSettings_WithOutOfRangeValue_ReturnsValidationProblemWithoutSidecarOrRestart()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            int restartCount = 0;
            host.RestartRequested += (sender, args) => Interlocked.Increment(ref restartCount);

            using HttpResponseMessage response = await client.PutAsync(EndToEndHostFactory.SettingsRelativeUrl, EndToEndHostFactory.CreateJsonContent(CreateValidSettingsBody(999)));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("errors").ValueKind);

            // No sidecar may appear and no restart may be raised for a rejected body.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Equal(0, Volatile.Read(ref restartCount));
            Assert.False(File.Exists(EndToEndBinState.SidecarPath));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PutSettings_WithNullEsclManualDevices_Returns400()
    {
        // FIXED (Phase 2 Batch 4, audit C-1b): the validator's list predicates are null-tolerant,
        // so a null list surfaces as a 400 ValidationProblem instead of an opaque 500, and
        // nothing is persisted.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            string requestJson = BuildNullDevicesSettingsJson();

            using HttpResponseMessage response = await client.PutAsync(
                EndToEndHostFactory.SettingsRelativeUrl,
                new StringContent(requestJson, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(File.Exists(EndToEndBinState.SidecarPath));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task DeleteOverrides_AfterPut_RemovesSidecarAndRaisesRestartEvent()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            int restartCount = 0;
            host.RestartRequested += (sender, args) => Interlocked.Increment(ref restartCount);

            using HttpResponseMessage putResponse = await client.PutAsync(EndToEndHostFactory.SettingsRelativeUrl, EndToEndHostFactory.CreateJsonContent(CreateValidSettingsBody(2000)));
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            Assert.True(File.Exists(EndToEndBinState.SidecarPath));

            using HttpResponseMessage deleteResponse = await client.DeleteAsync(EndToEndHostFactory.SettingsRelativeUrl + "/overrides");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            using (JsonDocument deleteDocument = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync()))
            {
                Assert.True(deleteDocument.RootElement.GetProperty("restarting").GetBoolean());
            }

            // PUT raises once and DELETE raises once; both land post-response, so poll for the pair.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref restartCount) < 2 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Equal(2, Volatile.Read(ref restartCount));
            Assert.False(File.Exists(EndToEndBinState.SidecarPath));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task DeleteOverrides_WithoutSidecar_ReportsNotRestartingWithoutEvent()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            int restartCount = 0;
            host.RestartRequested += (sender, args) => Interlocked.Increment(ref restartCount);

            using HttpResponseMessage deleteResponse = await client.DeleteAsync(EndToEndHostFactory.SettingsRelativeUrl + "/overrides");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            using (JsonDocument deleteDocument = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync()))
            {
                Assert.False(deleteDocument.RootElement.GetProperty("restarting").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(deleteDocument.RootElement.GetProperty("warning").GetString()));
            }

            // The no-op must not raise the restart event; give any stray raise a bounded window.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Equal(0, Volatile.Read(ref restartCount));
            Assert.False(File.Exists(EndToEndBinState.SidecarPath));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static ScannerSettingsDto CreateValidSettingsBody(int statusCheckInterval)
    {
        return new ScannerSettingsDto
        {
            StatusCheckInterval = statusCheckInterval,
            HttpTimeout = 500,
            StartupDelay = 0,
            DriverTimeoutMs = 3000,
            EsclSearchTimeoutMs = 1000,
            EsclSearchMarginMs = 500,
            DriverCooldownMs = 5000,
            DriverCooldownMaxMs = 10000,
            ScanQueueTimeoutMs = 1000,
            ScanOverallTimeoutMs = 30000,
            ScanNoProgressTimeoutMs = 10000,
            ShutdownTimeoutMs = 2000,
            ScannersRequestTimeoutSeconds = 15,
            ScanRequestTimeoutSeconds = 65,
            EsclManualDevices = []
        };
    }

    private static string BuildNullDevicesSettingsJson()
    {
        return "{\"statusCheckInterval\":1000,\"httpTimeout\":500,\"startupDelay\":0,\"driverTimeoutMs\":3000,"
            + "\"esclSearchTimeoutMs\":1000,\"esclSearchMarginMs\":500,\"driverCooldownMs\":5000,"
            + "\"driverCooldownMaxMs\":10000,\"scanQueueTimeoutMs\":1000,\"scanOverallTimeoutMs\":30000,"
            + "\"scanNoProgressTimeoutMs\":10000,\"shutdownTimeoutMs\":2000,\"scannersRequestTimeoutSeconds\":15,"
            + "\"scanRequestTimeoutSeconds\":65,\"esclManualDevices\":null}";
    }
}
