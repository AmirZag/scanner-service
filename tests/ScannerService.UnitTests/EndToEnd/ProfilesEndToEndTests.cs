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
public class ProfilesEndToEndTests : IDisposable
{
    private const string CreatedProfileName = "E2E CRUD Profile";
    private const string CreatedDeviceId = "11111111-1111-1111-1111-111111111111";
    private const string RenamedProfileName = "E2E CRUD Profile Renamed";

    public ProfilesEndToEndTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Fact]
    public async Task ProfileCrud_RoundTripThroughApi_PersistsUpdatesAndDeletesInRealSqlite()
    {
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);

            UpsertProfileDto createRequest = new UpsertProfileDto(CreatedProfileName, CreatedDeviceId);
            using HttpResponseMessage createResponse = await client.PostAsync("api/profiles", EndToEndHostFactory.CreateJsonContent(createRequest));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            string createdLocation = createResponse.Headers.Location?.ToString() ?? string.Empty;
            Assert.StartsWith("/api/profiles/", createdLocation, StringComparison.Ordinal);
            int profileId;
            using (JsonDocument createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()))
            {
                profileId = createDocument.RootElement.GetProperty("id").GetInt32();
                Assert.True(profileId > 0);
                Assert.Equal(CreatedProfileName, createDocument.RootElement.GetProperty("name").GetString());
                Assert.Equal(CreatedDeviceId, createDocument.RootElement.GetProperty("deviceId").GetString());
            }

            using (HttpResponseMessage listResponse = await client.GetAsync("api/profiles"))
            {
                Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                using JsonDocument listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
                Assert.True(ListContainsProfile(listDocument.RootElement, profileId, CreatedProfileName));
            }

            UpdateProfileDto updateRequest = new UpdateProfileDto { Name = RenamedProfileName, Resolution = 300 };
            using HttpResponseMessage updateResponse = await client.PatchAsync(FormattableString.Invariant($"api/profiles/{profileId}"), EndToEndHostFactory.CreateJsonContent(updateRequest));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            using (JsonDocument updateDocument = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal(RenamedProfileName, updateDocument.RootElement.GetProperty("name").GetString());
                Assert.Equal(300, updateDocument.RootElement.GetProperty("resolution").GetInt32());
                Assert.Equal(CreatedDeviceId, updateDocument.RootElement.GetProperty("deviceId").GetString());
            }

            using (HttpResponseMessage getResponse = await client.GetAsync(FormattableString.Invariant($"api/profiles/{profileId}")))
            {
                Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
                using JsonDocument getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                Assert.Equal(RenamedProfileName, getDocument.RootElement.GetProperty("name").GetString());
            }

            using HttpResponseMessage deleteResponse = await client.DeleteAsync(FormattableString.Invariant($"api/profiles/{profileId}"));
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            using (HttpResponseMessage missingResponse = await client.GetAsync(FormattableString.Invariant($"api/profiles/{profileId}")))
            {
                Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
            }

            using (HttpResponseMessage finalListResponse = await client.GetAsync("api/profiles"))
            {
                using JsonDocument finalListDocument = JsonDocument.Parse(await finalListResponse.Content.ReadAsStringAsync());
                Assert.False(ListContainsProfile(finalListDocument.RootElement, profileId, RenamedProfileName));
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task ProfileData_SurvivesFullHostStopStartCycle()
    {
        int firstPort = EndToEndHostFactory.FindFreeLoopbackPort();
        WebApiHostService firstHost = await EndToEndHostFactory.StartHostAsync(EndToEndHostFactory.CreateTestConfiguration(firstPort));
        int profileId;
        try
        {
            using HttpClient firstClient = EndToEndHostFactory.CreateClient(firstHost.ActualPort);
            UpsertProfileDto createRequest = new UpsertProfileDto(CreatedProfileName, CreatedDeviceId);
            using HttpResponseMessage createResponse = await firstClient.PostAsync("api/profiles", EndToEndHostFactory.CreateJsonContent(createRequest));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using JsonDocument createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            profileId = createDocument.RootElement.GetProperty("id").GetInt32();
        }
        finally
        {
            await firstHost.StopAsync();
            firstHost.Dispose();
        }

        ScannerServiceConfiguration secondConfiguration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService secondHost = await EndToEndHostFactory.StartHostAsync(secondConfiguration);
        try
        {
            using HttpClient secondClient = EndToEndHostFactory.CreateClient(secondHost.ActualPort);
            using HttpResponseMessage listResponse = await secondClient.GetAsync("api/profiles");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using JsonDocument listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());

            Assert.True(ListContainsProfile(listDocument.RootElement, profileId, CreatedProfileName));
        }
        finally
        {
            await secondHost.StopAsync();
            secondHost.Dispose();
        }
    }

    [Fact]
    public async Task PostProfile_WithDuplicateName_Returns409Conflict()
    {
        // FIXED (Phase 2 Batch 4, audit F-24): the endpoint translates the unique-index
        // DbUpdateException into a 409 Conflict instead of an opaque 500.
        ScannerServiceConfiguration configuration = EndToEndHostFactory.CreateTestConfiguration(EndToEndHostFactory.FindFreeLoopbackPort());
        WebApiHostService host = await EndToEndHostFactory.StartHostAsync(configuration);
        try
        {
            using HttpClient client = EndToEndHostFactory.CreateClient(host.ActualPort);
            UpsertProfileDto createRequest = new UpsertProfileDto(CreatedProfileName, CreatedDeviceId);

            using HttpResponseMessage firstResponse = await client.PostAsync("api/profiles", EndToEndHostFactory.CreateJsonContent(createRequest));
            using HttpResponseMessage secondResponse = await client.PostAsync("api/profiles", EndToEndHostFactory.CreateJsonContent(createRequest));

            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static bool ListContainsProfile(JsonElement profilesArray, int profileId, string expectedName)
    {
        foreach (JsonElement item in profilesArray.EnumerateArray())
        {
            if (item.GetProperty("id").GetInt32() == profileId)
            {
                Assert.Equal(expectedName, item.GetProperty("name").GetString());
                return true;
            }
        }

        return false;
    }
}
