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
        Assert.Null(body!.DefaultTextExtractionEngineId);
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
        Assert.Null(body!.DefaultTextExtractionEngineId);
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

    [Fact]
    public async Task PatchDocumentSettings_DefaultEngineDeleted_GetStillReturnsStaleId_ClearSucceeds()
    {
        var client = await CreateAdminClientAsync();

        // 1. Create a custom engine
        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Stale Engine Test",
            engineType = "Tesseract",
            configuration = "{\"languages\":[\"Eng\"]}"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        var engineId = created!.Id;

        // 2. Set it as default
        var patchResponse = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object)engineId.ToString() }
        });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // 3. Delete the engine
        var deleteResponse = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines/bulk-delete", new
        {
            ids = new[] { engineId }
        }, CT);
        deleteResponse.EnsureSuccessStatusCode();

        // 4. GET still returns the stale engine ID
        var getResponse = await client.GetAsync("/api/v1/admin/settings/documents", CT);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var settings = await getResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(engineId, settings!.DefaultTextExtractionEngineId);

        // 5. Clear the stale reference
        var clearResponse = await SendJsonPatchAsync(client, "/api/v1/admin/settings/documents", new object[]
        {
            new { op = "replace", path = "/defaultTextExtractionEngineId", value = (object?)null! }
        });
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        // 6. Verify it's cleared
        var verifyResponse = await client.GetAsync("/api/v1/admin/settings/documents", CT);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verified = await verifyResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Null(verified!.DefaultTextExtractionEngineId);
    }
}
