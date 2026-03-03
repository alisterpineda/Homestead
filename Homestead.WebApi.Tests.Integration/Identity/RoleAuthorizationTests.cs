using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Identity;

[Collection("Integration")]
public class RoleAuthorizationTests : IntegrationTestBase
{
    public RoleAuthorizationTests(PostgresFixture fixture) : base(fixture) { }

    // --- Admin endpoint access ---

    [Fact]
    public async Task Admin_CanAccessAdminEndpoints()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_Gets403OnAdminEndpoints()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_Gets403OnAdminEndpoints()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Guest_Gets403OnAdminEndpoints()
    {
        var client = await CreateGuestClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Document read access ---

    [Fact]
    public async Task Admin_CanReadDocuments()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CanReadDocuments()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanReadDocuments()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Guest_Gets403OnDocumentEndpoints()
    {
        var client = await CreateGuestClientAsync();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Document write access ---

    [Fact]
    public async Task Viewer_Gets403OnDocumentWriteEndpoints()
    {
        var client = await CreateViewerClientAsync();

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("test.pdf"), "name");
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "test.pdf");

        var response = await client.PostAsync("/api/v1/documents", form, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_Gets403OnTagCreate()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            name = "test-tag"
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_Gets403OnPartyCreate()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new
        {
            name = "test-party"
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Admin role management ---

    [Fact]
    public async Task Admin_CanUpdateUserRole()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "role-change@test.com",
            displayName = "Role Change",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.Equal("Contributor", created!.Role);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new
        {
            email = "role-change@test.com",
            displayName = "Role Change",
            role = "Viewer"
        }, CT);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.Equal("Viewer", updated!.Role);
    }

    // --- User-info returns correct role ---

    [Fact]
    public async Task UserInfo_ReturnsCorrectRole()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.Equal("Viewer", body!.Role);
    }

    [Fact]
    public async Task UserInfo_ReturnsAdminRole()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.Equal("Admin", body!.Role);
    }
}
