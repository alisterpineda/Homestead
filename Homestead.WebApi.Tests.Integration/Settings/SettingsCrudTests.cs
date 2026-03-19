using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Settings;

[Collection("Integration")]
public class SettingsCrudTests : IntegrationTestBase
{
    public SettingsCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetDocumentSettings_ReturnsDefaults_WhenNoRowExists()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/settings/documents", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(100, body!.MaxUploadSizeMb);
        Assert.Null(body.DefaultTextExtractionEngineId);
    }

    [Fact]
    public async Task PatchDocumentSettings_CreatesRowAndReturnsUpdatedValue()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)200 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(200, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task PatchDocumentSettings_PersistsAcrossRequests()
    {
        var client = await CreateAdminClientAsync();

        await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)300 }
        });

        var getResponse = await client.GetAsync("/api/v1/admin/settings/documents", CT);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(300, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task PatchDocumentSettings_SubsequentUpdate_ModifiesExistingRow()
    {
        var client = await CreateAdminClientAsync();

        await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)200 }
        });

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)500 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(500, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task PatchDocumentSettings_InvalidMaxUploadSize_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)0 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDocumentSettings_ExceedsMax_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/maxUploadSizeMb", value = (object)10001 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSystemSettings_ReturnsOk()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/settings/system", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatchSystemSettings_ReturnsOk()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/system",
            new { }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Settings_NonAdmin_Gets403()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/admin/settings/documents", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Settings_Anonymous_Gets401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/settings/documents", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchDocumentSettings_EmptyPatch_KeepsDefaults()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents",
            Array.Empty<object>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(100, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task PatchDocumentSettings_SetDefaultEngineToBuiltIn_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = "00000000-0000-0000-0000-000000000001";

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object)builtInId }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(Guid.Parse(builtInId), body!.DefaultTextExtractionEngineId);
    }

    [Fact]
    public async Task PatchDocumentSettings_SetDefaultEngineToNonExistent_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object)Guid.NewGuid().ToString() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDocumentSettings_SetAndClearDefaultEngine_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = "00000000-0000-0000-0000-000000000001";

        // Set
        await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object)builtInId }
        });

        // Clear
        var response = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new object[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object?)null! }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Null(body!.DefaultTextExtractionEngineId);
    }
}
