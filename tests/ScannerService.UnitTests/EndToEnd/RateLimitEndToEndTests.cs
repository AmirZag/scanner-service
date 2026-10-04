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
    public async Task GetHealth_WithUniqueForwardedForValues_LimitStillApplies()
    {
        // FIXED (Phase 2 Batch 4, audit S-1): the limiter keys on the connection's remote address
        // only, so rotating X-Forwarded-For values no longer buys fresh buckets - requests
        // 101..105 share the single loopback bucket and return 429 TooManyRequests.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);

            // The startup health poll shares the loopback bucket, so the exact request index of
            // the first 429 varies by a few: assert that it lands within the expected band and
            // that every request after it stays limited.
            int firstLimitedIndex = -1;
            for (int spoofedAddressSuffix = 1; spoofedAddressSuffix <= 105; spoofedAddressSuffix++)
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, EndToEndHostFactory.HealthRelativeUrl);
                request.Headers.Add("X-Forwarded-For", "203.0.113." + spoofedAddressSuffix.ToString(CultureInfo.InvariantCulture));

                using HttpResponseMessage response = await client.SendAsync(request);

                if (firstLimitedIndex < 0)
                {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        firstLimitedIndex = spoofedAddressSuffix;
                    }
                    else
                    {
                        Assert.True(spoofedAddressSuffix <= 75, $"Expected the limit to trip by request 75, got 429 at {spoofedAddressSuffix}");
                    }
                }
                else
                {
                    Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                }
            }

            Assert.InRange(firstLimitedIndex, 75, 105);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
