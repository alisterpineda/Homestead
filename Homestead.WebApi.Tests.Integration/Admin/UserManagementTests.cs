using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Admin;

[Collection("Integration")]
public class UserManagementTests : IntegrationTestBase
{
    public UserManagementTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetUsers_AsAdmin_ReturnsList()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<UserItem>>(CT);
        Assert.NotEmpty(body!.Items);
    }

    [Fact]
    public async Task GetUsers_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsAdmin_Returns201()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "newuser@test.com",
            displayName = "New User",
            password = "Password123!",
            role = "Contributor"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.Equal("newuser@test.com", body!.Email);
        Assert.Equal("New User", body.DisplayName);
        Assert.Equal("Contributor", body.Role);
        Assert.True(body.HasPassword);
    }

    [Fact]
    public async Task CreateUser_InvalidPassword_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "badpw@test.com",
            displayName = "Bad PW",
            password = "short",
            role = "Contributor"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AsAdmin_ModifiesUser()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "update-target@test.com",
            displayName = "Original",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}", new
        {
            email = "updated@test.com",
            displayName = "Updated Name",
            role = "Admin"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.Equal("Updated Name", body!.DisplayName);
        Assert.Equal("Admin", body.Role);
    }

    [Fact]
    public async Task DeleteUser_AsAdmin_Returns204()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "todelete@test.com",
            displayName = "Delete Me",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{created!.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_LastAdmin_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var usersResponse = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);
        var users = await usersResponse.Content.ReadFromJsonAsync<PaginatedResult<UserItem>>(CT);
        var adminUser = users!.Items.First(u => u.Role == "Admin");

        var response = await client.DeleteAsync($"/api/v1/admin/users/{adminUser.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkDeleteUsers_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();

        var r1 = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "bulk1@test.com",
            displayName = "Bulk 1",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        var u1 = await r1.Content.ReadFromJsonAsync<UserItem>(CT);

        var r2 = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "bulk2@test.com",
            displayName = "Bulk 2",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        var u2 = await r2.Content.ReadFromJsonAsync<UserItem>(CT);

        var response = await client.PostAsJsonAsync("/api/v1/admin/users/bulk-delete", new
        {
            ids = new[] { u1!.Id, u2!.Id }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    [Fact]
    public async Task AdminEndpoints_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ReturnsIsActiveTrue()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "active-check@test.com",
            displayName = "Active Check",
            password = "Password123!",
            role = "Contributor"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.True(body!.IsActive);
    }

    [Fact]
    public async Task GetUsers_ReturnsIsActiveField()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<UserItem>>(CT);
        Assert.All(body!.Items, u => Assert.True(u.IsActive));
    }

    [Fact]
    public async Task UpdateUser_CanToggleIsActive()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "toggle-active@test.com",
            displayName = "Toggle Active",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.True(created!.IsActive);

        // Deactivate
        var deactivateResponse = await client.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new
        {
            email = "toggle-active@test.com",
            displayName = "Toggle Active",
            role = "Contributor",
            isActive = false
        }, CT);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.False(deactivated!.IsActive);

        // Re-activate
        var activateResponse = await client.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new
        {
            email = "toggle-active@test.com",
            displayName = "Toggle Active",
            role = "Contributor",
            isActive = true
        }, CT);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.True(activated!.IsActive);
    }

    [Fact]
    public async Task ChangeUserPassword_AsAdmin_Returns204()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "changepw@test.com",
            displayName = "Change PW",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}/password", new
        {
            password = "NewPassword123!"
        }, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserPassword_InvalidPassword_ReturnsBadRequest()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "changepw-invalid@test.com",
            displayName = "Change PW Invalid",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}/password", new
        {
            password = "short"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserPassword_NonExistentUser_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/password", new
        {
            password = "NewPassword123!"
        }, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserPassword_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/password", new
        {
            password = "NewPassword123!"
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserPassword_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/password", new
        {
            password = "NewPassword123!"
        }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserPassword_ThenLoginWithNewPassword()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "changepw-login@test.com",
            displayName = "Change PW Login",
            password = "Password123!",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        var changeResponse = await client.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}/password", new
        {
            password = "NewPassword123!"
        }, CT);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Login with new password
        var loginClient = CreateAnonymousClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = "changepw-login@test.com",
            password = "NewPassword123!"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_SelfDeactivate_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var userId = await GetUserIdAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{userId}", new
        {
            email = "admin@test.com",
            displayName = "Admin",
            role = "Admin",
            isActive = false
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_SelfDemoteRole_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var userId = await GetUserIdAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{userId}", new
        {
            email = "admin@test.com",
            displayName = "Admin",
            role = "Contributor",
            isActive = true
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_SelfUpdateSafeFields_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var userId = await GetUserIdAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{userId}", new
        {
            email = "admin@test.com",
            displayName = "Updated Admin Name",
            role = "Admin",
            isActive = true
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.Equal("Updated Admin Name", body!.DisplayName);
    }

}
