using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using ScannerService.Domain.Common;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class HostLifecycleEndToEndTests : IDisposable
{
    public HostLifecycleEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task StartAsync_WithFreeConfiguredPort_ListensOnConfiguredPortAndServesHealth()
    {
        int configuredPort = EndToEndHostFactory.FindFreeLoopbackPort();
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(configuredPort);
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);
            string payload = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            Assert.True(host.IsRunning);
            Assert.Equal(configuredPort, host.ActualPort);
            Assert.Equal(configuredPort, host.ConfiguredPort);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.Contains("X-Correlation-ID"));
            Assert.True(root.GetProperty("isRunning").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_AfterStart_ServesDocumentedApiSurface()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync("openapi/openapi.json");
            string payload = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(JsonValueKind.Object, root.GetProperty("paths").ValueKind);
            Assert.True(root.GetProperty("paths").TryGetProperty("/api/health", out _));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GetDetailedHealth_WithMachineDependencies_StatusMatchesDependencyHealth()
    {
        // FIXED (audit AR-1): the status is carried by the result itself (200 healthy /
        // 503 unhealthy via TypedResults.Json), replacing the previously pinned always-200
        // behavior. Whether the host is healthy depends on the machine it runs on - a LAN
        // with discoverable eSCL devices yields a healthy Scanners dependency while a
        // scannerless CI VM does not - so the status is asserted against the reported
        // dependencies rather than a fixed code, keeping the test environment-independent.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync("api/health/detailed");
            string payload = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            bool allHealthy = true;
            foreach (JsonProperty dependency in root.GetProperty("dependencies").EnumerateObject())
            {
                allHealthy &= dependency.Value.GetBoolean();
            }

            HttpStatusCode expected = allHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
            Assert.Equal(expected, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("correlationId").GetString()));
            Assert.Equal(JsonValueKind.Object, root.GetProperty("dependencies").ValueKind);
            Assert.True(root.GetProperty("dependencies").TryGetProperty("Database", out _));
            Assert.True(root.GetProperty("dependencies").TryGetProperty("Scanners", out _));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhenHostAlreadyRunning_ReturnsImmediatelyWithoutChangingPort()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            int portBeforeSecondStart = host.ActualPort;

            await host.StartAsync();

            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

            Assert.True(host.IsRunning);
            Assert.Equal(portBeforeSecondStart, host.ActualPort);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_AfterRunningStart_ClosesListenerAndRepeatedStopsAreNoOps()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);

            await host.StopAsync();

            Assert.False(host.IsRunning);
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(EndToEndHostFactory.HealthRelativeUrl));

            await host.StopAsync();

            Assert.False(host.IsRunning);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Dispose_WithoutPriorStop_CompletesIsIdempotentAndClosesListener()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);

        host.Dispose();
        host.Dispose();

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(EndToEndHostFactory.HealthRelativeUrl));
    }

    [Fact]
    public async Task StartAsync_WhenConfiguredPortIsOccupied_FallsBackToNextFreePort()
    {
        int occupiedPort = EndToEndHostFactory.FindFreeLoopbackPort();
        using TcpListener blocker = new TcpListener(IPAddress.Loopback, occupiedPort);
        blocker.Start();
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(occupiedPort);
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

            Assert.NotEqual(occupiedPort, host.ActualPort);
            Assert.Equal(occupiedPort, host.ConfiguredPort);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhenWildcardListenerOccupiesConfiguredPort_FallsBackToNextFreePort()
    {
        // A 0.0.0.0 listener occupies the port on every address, so the loopback port probe must
        // classify it as conflicting and steer the fallback search away from it.
        int occupiedPort = EndToEndHostFactory.FindFreeLoopbackPort();
        using TcpListener wildcardBlocker = new TcpListener(IPAddress.Any, occupiedPort);
        wildcardBlocker.Start();
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(occupiedPort);
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

            Assert.NotEqual(occupiedPort, host.ActualPort);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhenEverySearchablePortIsOccupied_FallsBackToEphemeralPort()
    {
        int blockStart = FindConsecutiveFreeLoopbackPortBlock(ApplicationConstants.Ports.MaxPortSearchRange + 1);
        List<TcpListener> blockers = new List<TcpListener>();
        try
        {
            for (int port = blockStart; port <= blockStart + ApplicationConstants.Ports.MaxPortSearchRange; port++)
            {
                TcpListener blocker = new TcpListener(IPAddress.Loopback, port);
                blocker.Start();
                blockers.Add(blocker);
            }

            ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(blockStart);
            WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
            try
            {
                using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
                using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

                for (int port = blockStart; port <= blockStart + ApplicationConstants.Ports.MaxPortSearchRange; port++)
                {
                    Assert.NotEqual(port, host.ActualPort);
                }

                Assert.Equal(blockStart, host.ConfiguredPort);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
        finally
        {
            foreach (TcpListener blocker in blockers)
            {
                blocker.Stop();
            }
        }
    }

    [Fact]
    public async Task GetScalarUi_AfterStart_ServesInteractiveReferenceShell()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync("scalar");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(body);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WithWildcardApiHost_StartsBeyondLoopbackAndServesHealth()
    {
        // The wildcard bind runs the beyond-loopback warning path (including the firewall step,
        // which skips because the packaged executable is absent from the test bin) and the
        // manual-eSCL-device logging path. The test bin cleanup removes the executable before
        // and after this test, so no netsh invocation and no elevation prompt can happen.
        int configuredPort = EndToEndHostFactory.FindFreeLoopbackPort();
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(configuredPort);
        configuration.ApiHost = "*";
        configuration.EsclManualDevices = new List<EsclManualDeviceConfiguration>
        {
            new EsclManualDeviceConfiguration { Address = "http://127.0.0.1:8080/eSCL" }
        };
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

            Assert.True(host.IsRunning);
            Assert.True(host.ActualPort >= 1024);
            Assert.Equal("localhost", host.LocalUrlHost);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WithSpecificLanAddress_BindsThatAddressAndServesHealth()
    {
        IPAddress? lanAddress = DiscoverNonLoopbackIPv4Address();
        if (lanAddress is null)
        {
            // No non-loopback IPv4 address is assigned on this machine; the widened-bind matrix
            // is covered by the wildcard test instead.
            return;
        }

        int configuredPort = EndToEndHostFactory.FindFreeLoopbackPort();
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(configuredPort);
        configuration.ApiHost = lanAddress.ToString();
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        try
        {
            await host.StartAsync();
            using HttpClient client = new HttpClient
            {
                BaseAddress = new Uri("http://" + lanAddress.ToString() + ":" + host.ActualPort.ToString(CultureInfo.InvariantCulture) + "/"),
                Timeout = TimeSpan.FromSeconds(25)
            };
            await WaitUntilHealthyOnAsync(client);
            using HttpResponseMessage response = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);

            Assert.True(host.IsRunning);
            Assert.Equal(lanAddress.ToString(), host.LocalUrlHost);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhenApiHostIsNotAssignedToAnyAdapter_ThrowsWithAddressGuidance()
    {
        // 192.0.2.17 belongs to TEST-NET-1 (reserved for documentation) and is never assigned to
        // an adapter, so the port probe fails with SocketError.AddressNotAvailable and the
        // host-start error mapping must translate that into actionable guidance.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        configuration.ApiHost = "192.0.2.17";
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        try
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

            Assert.Contains("is not assigned to any network adapter", failure.Message, StringComparison.Ordinal);
            Assert.False(host.IsRunning);

            // FIXED (Phase 2 Batch 3, audit B-3): the failed start must leave nothing behind -
            // before the fix, StopAsync's !IsRunning gate made the cleanup a silent no-op and the
            // built host and CTS leaked on every failed start (and every failed rollback).
            Assert.Null(ReadPrivateField(host, "_app"));
            Assert.Null(ReadPrivateField(host, "_cts"));
            Assert.Null(ReadPrivateField(host, "_runTask"));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static object? ReadPrivateField(WebApiHostService host, string fieldName)
    {
        FieldInfo field = typeof(WebApiHostService).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Private field " + fieldName + " not found");
        return field.GetValue(host);
    }

    [Fact]
    public async Task StartAsync_WhenLoggingConfigurationIsInvalid_ThrowsAndFallsThroughToGenericErrorPath()
    {
        // LoadLoggingConfiguration reads appsettings.json straight from the test bin; an invalid
        // Logging:File section makes the startup validation throw before any listener exists, and
        // the generic catch must log, run the (no-op) bounded stop and rethrow.
        const string invalidLoggingJson = """
            {
              "ScannerService": {},
              "Logging": {
                "File": {
                  "Path": "logs/scanner-.log",
                  "RollingInterval": "fortnight",
                  "RetainedFileCountLimit": 0,
                  "FileSizeLimitBytes": 10
                }
              }
            }
            """;
        string baseSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string backupPath = baseSettingsPath + ".coverage-backup";
        File.Move(baseSettingsPath, backupPath, true);
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        var settingsStore = new LocalSettingsStore(configuration);
        var host = new WebApiHostService(configuration, settingsStore);
        try
        {
            File.WriteAllText(baseSettingsPath, invalidLoggingJson);
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

            Assert.Contains("Logging configuration validation failed", failure.Message, StringComparison.Ordinal);
            Assert.Contains("RollingInterval", failure.Message, StringComparison.Ordinal);
            Assert.False(host.IsRunning);
        }
        finally
        {
            File.Move(backupPath, baseSettingsPath, true);
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static async Task WaitUntilHealthyOnAsync(HttpClient client)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage probe = await client.GetAsync(EndToEndHostFactory.HealthRelativeUrl);
                if (probe.IsSuccessStatusCode)
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

        throw new InvalidOperationException("The API host did not become healthy on the specific address within 15 seconds");
    }

    private static IPAddress? DiscoverNonLoopbackIPv4Address()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation addressInfo in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (addressInfo.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(addressInfo.Address))
                {
                    return addressInfo.Address;
                }
            }
        }

        return null;
    }

    private static int FindConsecutiveFreeLoopbackPortBlock(int portCount)
    {
        // A low, rarely-used region keeps the occupied block disjoint from the OS ephemeral port
        // range, so the temp-listener fallback port can never collide with a blocked port.
        const int searchFloor = 18000;
        for (int blockStart = searchFloor; blockStart + portCount < 65536; blockStart += portCount)
        {
            bool allFree = true;
            List<TcpListener> probes = new List<TcpListener>();
            for (int port = blockStart; port < blockStart + portCount && allFree; port++)
            {
                TcpListener probe = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    probe.Start();
                    probes.Add(probe);
                }
                catch (SocketException)
                {
                    allFree = false;
                }
            }

            foreach (TcpListener probe in probes)
            {
                probe.Stop();
            }

            if (allFree)
            {
                return blockStart;
            }
        }

        throw new InvalidOperationException("No consecutive free loopback port block was found");
    }
}
