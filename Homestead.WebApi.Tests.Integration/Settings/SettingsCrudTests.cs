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
    }

    [Fact]
    public async Task UpdateDocumentSettings_CreatesRowAndReturnsUpdatedValue()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 200
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(200, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task UpdateDocumentSettings_PersistsAcrossRequests()
    {
        var client = await CreateAdminClientAsync();

        await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 300
        }, CT);

        var getResponse = await client.GetAsync("/api/v1/admin/settings/documents", CT);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(300, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task UpdateDocumentSettings_SubsequentUpdate_ModifiesExistingRow()
    {
        var client = await CreateAdminClientAsync();

        await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 200
        }, CT);

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 500
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(500, body!.MaxUploadSizeMb);
    }

    [Fact]
    public async Task UpdateDocumentSettings_InvalidMaxUploadSize_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 0
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDocumentSettings_ExceedsMax_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/documents", new
        {
            maxUploadSizeMb = 10001
        }, CT);

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
    public async Task UpdateSystemSettings_ReturnsOk()
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
    public async Task UpdateDocumentSettings_NullField_KeepsDefault()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/documents",
            new { }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal(100, body!.MaxUploadSizeMb);
    }
}
