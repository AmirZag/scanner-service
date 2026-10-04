using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class RateLimitEndToEndTests : IDisposable
{
    public RateLimitEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task GetHealth_WithUniqueForwardedForValues_CurrentBehavior_BypassesRateLimit()
    {
        // KNOWN BUG S-1: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // RateLimitMiddleware keys every request that carries X-Forwarded-For into its own bucket,
        // so one client can outrun the 100-requests-per-minute limit by rotating header values.
        // With 105 unique values each request lands in a fresh bucket and every response is 200;
        // after the fix (keying on RemoteIpAddress only), requests 101..105 share the single
        // loopback bucket and return 429 TooManyRequests - assert that here instead.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);

            for (int spoofedAddressSuffix = 1; spoofedAddressSuffix <= 105; spoofedAddressSuffix++)
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, EndToEndHostFactory.HealthRelativeUrl);
                request.Headers.Add("X-Forwarded-For", "203.0.113." + spoofedAddressSuffix.ToString(CultureInfo.InvariantCulture));

                using HttpResponseMessage response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
