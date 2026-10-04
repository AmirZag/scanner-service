using System.Net;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using Xunit;

namespace ScannerService.UnitTests.ApiIntegration;

public sealed class ProfileEndpointTests : IAsyncLifetime
{
    private const string ValidProfileJson = """
        {
            "name": "Front Desk",
            "deviceId": "twain-device-1",
            "paperSource": "Feeder",
            "bitDepth": "Grayscale",
            "pageSize": "Letter",
            "horizontalAlign": "Left",
            "resolution": 300,
            "scale": "1:2",
            "brightness": 10,
            "contrast": -10,
            "imageQuality": 95
        }
        """;

    private SqliteConnection _sqliteConnection = null!;
    private HostFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();
        _fixture = await TestApiHost.CreateAsync(services =>
        {
            services.AddDbContext<Context>(options => options.UseSqlite(_sqliteConnection));
            services.AddMemoryCache();
            services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddScoped<IProfileRepository, ProfileRepository>();
        });

        await using AsyncServiceScope scope = _fixture.App.Services.CreateAsyncScope();
        Context context = scope.ServiceProvider.GetRequiredService<Context>();
        await context.Database.EnsureCreatedAsync();

        _client = _fixture.Client;
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        if (_sqliteConnection is not null)
        {
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetProfiles_EmptyDatabase_ReturnsEmptyArray()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<ProfileDto> profiles = await TestApiHost.ReadJsonAsync<List<ProfileDto>>(response);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task GetProfiles_AfterCreations_ReturnsAllProfiles()
    {
        ProfileDto first = await CreateProfileAsync("""{"name": "First Profile", "deviceId": "device-1"}""");
        ProfileDto second = await CreateProfileAsync("""{"name": "Second Profile", "deviceId": "device-2"}""");

        using HttpResponseMessage response = await _client.GetAsync("/api/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<ProfileDto> profiles = await TestApiHost.ReadJsonAsync<List<ProfileDto>>(response);
        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, profile => profile.Id == first.Id && profile.Name == "First Profile");
        Assert.Contains(profiles, profile => profile.Id == second.Id && profile.Name == "Second Profile");
    }

    [Fact]
    public async Task GetProfile_ExistingId_ReturnsProfile()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage response = await _client.GetAsync($"/api/profiles/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProfileDto fetched = await TestApiHost.ReadJsonAsync<ProfileDto>(response);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Front Desk", fetched.Name);
        Assert.Equal("Feeder", fetched.PaperSource);
    }

    [Fact]
    public async Task GetProfile_UnknownId_Returns404WithError()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/profiles/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Profile 999 not found", await TestApiHost.ReadErrorAsync(response));
    }

    [Fact]
    public async Task PostProfile_ValidBody_Returns201WithEchoAndLocation()
    {
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(_client, "/api/profiles", ValidProfileJson);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProfileDto created = await TestApiHost.ReadJsonAsync<ProfileDto>(response);
        Assert.True(created.Id > 0);
        Assert.Equal("Front Desk", created.Name);
        Assert.Equal("twain-device-1", created.DeviceId);
        Assert.Equal("Feeder", created.PaperSource);
        Assert.Equal("Grayscale", created.BitDepth);
        Assert.Equal("Letter", created.PageSize);
        Assert.Equal("Left", created.HorizontalAlign);
        Assert.Equal(300, created.Resolution);
        Assert.Equal("1:2", created.Scale);
        Assert.Equal(10, created.Brightness);
        Assert.Equal(-10, created.Contrast);
        Assert.Equal(95, created.ImageQuality);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.NotEqual(default, created.UpdatedAt);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/profiles/{created.Id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PostProfile_EmptyName_Returns400ValidationProblemWithNameKey()
    {
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(
            _client, "/api/profiles", """{"name": "", "deviceId": "twain-device-1"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("Name", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("Profile Name Is Required", messages);
    }

    [Fact]
    public async Task PostProfile_ResolutionOutOfRange_Returns400ValidationProblemWithResolutionKey()
    {
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(
            _client, "/api/profiles", """{"name": "Bad Resolution", "resolution": 5000}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("Resolution", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("Resolution must be between 50 and 1200 DPI", messages);
    }

    [Theory]
    [InlineData("paperSource", "Table", "PaperSource", "PaperSource must be either 'Glass' or 'Feeder'")]
    [InlineData("bitDepth", "UltraColor", "BitDepth", "BitDepth must be 'Color', 'Grayscale', or 'BlackAndWhite'")]
    [InlineData("scale", "1:3", "Scale", "Scale must be one of: 1:1, 1:2, 1:4, 1:8")]
    public async Task PostProfile_DisallowedValue_Returns400WithFieldKey(
        string jsonField,
        string badValue,
        string expectedPropertyKey,
        string expectedMessage)
    {
        string json = $$"""
            {
                "name": "Validator Probe",
                "deviceId": "twain-device-1",
                "pageSize": "A4",
                "horizontalAlign": "Center",
                "resolution": 200,
                "brightness": 0,
                "contrast": 0,
                "imageQuality": 85,
                "{{jsonField}}": "{{badValue}}"
            }
            """;

        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(_client, "/api/profiles", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue(expectedPropertyKey, out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains(expectedMessage, messages);
    }

    [Fact]
    public async Task PostProfile_EmptyDeviceId_IsAccepted_CurrentBehavior()
    {
        // KNOWN BUG C-1c: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // UpsertProfileValidator has no DeviceId rule (UpdateProfileValidator does), so POSTing an
        // empty deviceId creates a profile that can never scan. Once the rule exists this must
        // assert HttpStatusCode.BadRequest (400) instead of Created (201).
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(
            _client, "/api/profiles", """{"name": "No Device", "deviceId": ""}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProfileDto created = await TestApiHost.ReadJsonAsync<ProfileDto>(response);
        Assert.Equal(string.Empty, created.DeviceId);
    }

    [Fact]
    public async Task PostProfile_DuplicateName_ThrowsDbUpdateException_CurrentBehavior()
    {
        // KNOWN BUG F-24: pins current (buggy) behavior; flip this assertion when the bug is fixed.
        // The unique index on Profile.Name turns the second POST into a raw DbUpdateException that
        // escapes the endpoint; TestServer propagates handler exceptions to the awaiting client.
        // Once fixed (pre-check or catch mapped to 400/409), the second POST must return a status
        // code instead of throwing.
        await CreateProfileAsync("""{"name": "Duplicate Name", "deviceId": "device-1"}""");

        await Assert.ThrowsAsync<DbUpdateException>(
            () => TestApiHost.PostJsonAsync(_client, "/api/profiles", """{"name": "Duplicate Name", "deviceId": "device-2"}"""));
    }

    [Fact]
    public async Task PatchProfile_PartialBody_OnlyProvidedFieldsChange()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage patchResponse = await TestApiHost.PatchJsonAsync(
            _client, $"/api/profiles/{created.Id}", """{"name": "Renamed Profile", "resolution": 600}""");

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        ProfileDto updated = await TestApiHost.ReadJsonAsync<ProfileDto>(patchResponse);
        Assert.Equal("Renamed Profile", updated.Name);
        Assert.Equal(600, updated.Resolution);
        Assert.Equal("twain-device-1", updated.DeviceId);
        Assert.Equal("Feeder", updated.PaperSource);
        Assert.Equal("Grayscale", updated.BitDepth);
        Assert.Equal("Letter", updated.PageSize);
        Assert.Equal("Left", updated.HorizontalAlign);
        Assert.Equal("1:2", updated.Scale);
        Assert.Equal(10, updated.Brightness);
        Assert.Equal(-10, updated.Contrast);
        Assert.Equal(95, updated.ImageQuality);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);

        using HttpResponseMessage getResponse = await _client.GetAsync($"/api/profiles/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        ProfileDto fetched = await TestApiHost.ReadJsonAsync<ProfileDto>(getResponse);
        Assert.Equal("Renamed Profile", fetched.Name);
        Assert.Equal(600, fetched.Resolution);
    }

    [Fact]
    public async Task PatchProfile_EmptyDeviceId_Returns400()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage response = await TestApiHost.PatchJsonAsync(
            _client, $"/api/profiles/{created.Id}", """{"deviceId": ""}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("DeviceId", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("DeviceId cannot be empty or whitespace", messages);
    }

    [Fact]
    public async Task PatchProfile_DisallowedBitDepth_Returns400()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage response = await TestApiHost.PatchJsonAsync(
            _client, $"/api/profiles/{created.Id}", """{"bitDepth": "XRay"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Dictionary<string, string[]> problems = await TestApiHost.ReadValidationProblemsAsync(response);
        Assert.True(problems.TryGetValue("BitDepth", out string[]? messages));
        Assert.NotNull(messages);
        Assert.Contains("BitDepth must be 'Color', 'Grayscale', or 'BlackAndWhite'", messages);
    }

    [Fact]
    public async Task PatchProfile_UnknownId_Returns404()
    {
        using HttpResponseMessage response = await TestApiHost.PatchJsonAsync(_client, "/api/profiles/888", "{}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Profile 888 not found", await TestApiHost.ReadErrorAsync(response));
    }

    [Fact]
    public async Task PatchProfile_EmptyBody_ChangesNothingAndReturns200()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage response = await TestApiHost.PatchJsonAsync(
            _client, $"/api/profiles/{created.Id}", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProfileDto updated = await TestApiHost.ReadJsonAsync<ProfileDto>(response);
        Assert.Equal(created.Name, updated.Name);
        Assert.Equal(created.DeviceId, updated.DeviceId);
        Assert.Equal(created.PaperSource, updated.PaperSource);
        Assert.Equal(created.BitDepth, updated.BitDepth);
        Assert.Equal(created.Resolution, updated.Resolution);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task DeleteProfile_ExistingId_Returns204ThenGetReturns404()
    {
        ProfileDto created = await CreateProfileAsync(ValidProfileJson);

        using HttpResponseMessage deleteResponse = await _client.DeleteAsync($"/api/profiles/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage getResponse = await _client.GetAsync($"/api/profiles/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteProfile_UnknownId_Returns404WithError()
    {
        using HttpResponseMessage response = await _client.DeleteAsync("/api/profiles/777");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Profile 777 not found", await TestApiHost.ReadErrorAsync(response));
    }

    private async Task<ProfileDto> CreateProfileAsync(string json)
    {
        using HttpResponseMessage response = await TestApiHost.PostJsonAsync(_client, "/api/profiles", json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await TestApiHost.ReadJsonAsync<ProfileDto>(response);
    }
}
