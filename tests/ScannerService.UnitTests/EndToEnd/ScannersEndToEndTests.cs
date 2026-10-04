using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class ScannersEndToEndTests : IDisposable
{
    public ScannersEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task GetScanners_WithTinyDriverBudgets_ReturnsArrayWithinRequestBudget()
    {
        // The endpoint runs the REAL NAPS2 enumeration with the tiny test budgets (3s driver
        // budget, 1s eSCL window) under the 15s request-timeout policy. Device contents vary per
        // machine (TWAIN/WIA/eSCL devices, manual entries), so only the response shape is asserted.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync("api/scanners");
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(JsonValueKind.Array, root.ValueKind);
            foreach (JsonElement item in root.EnumerateArray())
            {
                Assert.Equal(JsonValueKind.String, item.GetProperty("id").ValueKind);
                Assert.Equal(JsonValueKind.String, item.GetProperty("name").ValueKind);
                Assert.Equal(JsonValueKind.String, item.GetProperty("driver").ValueKind);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GetScanners_ListsManuallyConfiguredEsclDevices()
    {
        // Manual eSCL devices are pure configuration (no discovery, no hardware): they must show
        // up in the listing with their configured address as the stable device id, regardless of
        // what the driver enumeration found on this machine.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        configuration.EsclManualDevices =
        [
            new EsclManualDeviceConfiguration { Name = "Test Manual eSCL", Address = "http://127.0.0.1:9" }
        ];
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.GetAsync("api/scanners");
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bool manualListed = false;
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.GetProperty("id").GetString() == "http://127.0.0.1:9")
                {
                    manualListed = true;
                    Assert.Equal("Test Manual eSCL", item.GetProperty("name").GetString());
                    Assert.Equal("Escl", item.GetProperty("driver").GetString());
                }
            }

            Assert.True(manualListed, "Expected the manually configured eSCL device in the listing");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PostScannersRefresh_AfterStart_ReturnsOkAcknowledgement()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpResponseMessage response = await client.PostAsync("api/scanners/refresh", content: null);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PostScan_WithUnknownProfileId_ReturnsBadRequestWithoutDeviceTouch()
    {
        // Profile lookup (Context.Profiles.FindAsync) precedes any device access in
        // ScanJobService.StartScanJobAsync, so an unknown id fails before a scanner is touched:
        // this contract is safe to exercise on hardware-less machines.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            ScanRequestDto scanRequest = new ScanRequestDto(987654321);

            using HttpResponseMessage response = await client.PostAsync("api/scan", EndToEndHostFactory.CreateJsonContent(scanRequest));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("987654321", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
