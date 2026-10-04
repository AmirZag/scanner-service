using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;

namespace ScannerService.UnitTests.EndToEnd;

/// <summary>
/// Builds the real composition root (WebApiHostService with Kestrel, the real SQLite database and
/// the real settings sidecar) for end-to-end tests. Configuration values keep every driver budget
/// tiny while staying inside ConfigurationValidator's ranges, so the same numbers are valid both
/// for host startup and for settings PUT bodies. The bind never widens beyond localhost, which
/// also keeps ApiListenerFirewall out of the start path.
/// </summary>
internal static class EndToEndHostFactory
{
    public const string HealthRelativeUrl = "api/health";
    public const string SettingsRelativeUrl = "api/settings";

    public static ScannerServiceConfiguration CreateTestConfiguration(int apiPort)
    {
        return new ScannerServiceConfiguration
        {
            ApiPort = apiPort,
            ApiHost = "localhost",
            StatusCheckInterval = 1000,
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

    public static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public static HttpClient CreateClient(int port)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(string.Format(CultureInfo.InvariantCulture, "http://127.0.0.1:{0}/", port)),
            Timeout = TimeSpan.FromSeconds(25)
        };
    }

    public static async Task<WebApiHostService> StartHostAsync(ScannerServiceConfiguration configuration)
    {
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        try
        {
            await host.StartAsync();
            using HttpClient probe = CreateClient(host.ActualPort);
            await WaitUntilHealthyAsync(probe);
            return host;
        }
        catch
        {
            await host.StopAsync();
            host.Dispose();
            throw;
        }
    }

    public static async Task WaitUntilHealthyAsync(HttpClient client)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(HealthRelativeUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The host is not accepting connections yet; keep polling until the deadline.
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("The API host did not become healthy within 30 seconds");
    }

    public static StringContent CreateJsonContent(object payload)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string json = JsonSerializer.Serialize(payload, options);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
