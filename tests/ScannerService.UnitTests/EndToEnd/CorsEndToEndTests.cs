using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class CorsEndToEndTests : IDisposable
{
    public CorsEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task OptionsPreflight_WithArbitraryForeignOrigin_ReflectsOriginWithoutCredentials()
    {
        // FIXED (Phase 2 Batch 4, audit S-2): AllowCredentials was removed from the default policy
        // (the API is unauthenticated by design, so credentialed cross-origin reads bought nothing
        // and gave every web page cookie-flavored reach). Origins stay allow-all for local tools;
        // the preflight still approves the request, but never advertises credentials.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            using HttpRequestMessage preflight = new HttpRequestMessage(HttpMethod.Options, "api/profiles");
            preflight.Headers.Add("Origin", "http://evil.example");
            preflight.Headers.Add("Access-Control-Request-Method", "PUT");

            using HttpResponseMessage response = await client.SendAsync(preflight);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("http://evil.example", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"), "AllowCredentials must stay off the unauthenticated API");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
