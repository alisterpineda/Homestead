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

        var response = await client.GetAsync("/api/v1/documents/settings", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Null(body!.AutoClassifyOnUpload);
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

        var response = await client.GetAsync("/api/v1/documents/settings", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Settings_Anonymous_Gets401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/documents/settings", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchDocumentSettings_EmptyPatch_KeepsDefaults()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/documents/settings",
            Array.Empty<object>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Null(body!.AutoClassifyOnUpload);
    }

    [Fact]
    public async Task PatchDocumentSettings_SetAndClearAutoClassify_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        // Set
        var setResponse = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new[]
        {
            new { op = "replace", path = "/autoClassifyOnUpload", value = (object)true }
        });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var setBody = await setResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.True(setBody!.AutoClassifyOnUpload);

        // Clear
        var clearResponse = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new object[]
        {
            new { op = "replace", path = "/autoClassifyOnUpload", value = (object?)null! }
        });
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var clearBody = await clearResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Null(clearBody!.AutoClassifyOnUpload);
    }
}
