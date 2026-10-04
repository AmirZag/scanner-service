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
    public async Task OptionsPreflight_WithArbitraryForeignOrigin_CurrentBehavior_ReflectsOriginWithCredentials()
    {
        // KNOWN BUG S-2: pins current (buggy) behavior; flip these assertions when the bug is fixed.
        // The default CORS policy allows every origin (SetIsOriginAllowed(_ => true)) together with
        // AllowCredentials on an unauthenticated API, so any web page gets its own origin echoed and
        // preflight approval for PUT/DELETE. After the fix, the foreign origin must be refused:
        // assert that Access-Control-Allow-Origin (and -Credentials) are absent instead.
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
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
