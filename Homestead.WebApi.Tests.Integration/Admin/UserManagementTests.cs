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

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<UserItem>>();
        body!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetUsers_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
            isAdmin = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<UserItem>();
        body!.Email.Should().Be("newuser@test.com");
        body.DisplayName.Should().Be("New User");
        body.IsAdmin.Should().BeFalse();
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
            isAdmin = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
            isAdmin = false
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>();

        var response = await client.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}", new
        {
            email = "updated@test.com",
            displayName = "Updated Name",
            isAdmin = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserItem>();
        body!.DisplayName.Should().Be("Updated Name");
        body.IsAdmin.Should().BeTrue();
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
            isAdmin = false
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>();

        var response = await client.DeleteAsync($"/api/v1/admin/users/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteUser_LastAdmin_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var usersResponse = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10");
        var users = await usersResponse.Content.ReadFromJsonAsync<PaginatedResult<UserItem>>();
        var adminUser = users!.Items.First(u => u.IsAdmin);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{adminUser.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
            isAdmin = false
        });
        var u1 = await r1.Content.ReadFromJsonAsync<UserItem>();

        var r2 = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "bulk2@test.com",
            displayName = "Bulk 2",
            password = "Password123!",
            isAdmin = false
        });
        var u2 = await r2.Content.ReadFromJsonAsync<UserItem>();

        var response = await client.PostAsJsonAsync("/api/v1/admin/users/bulk-delete", new
        {
            ids = new[] { u1!.Id, u2!.Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>();
        body!.DeletedCount.Should().Be(2);
    }

    [Fact]
    public async Task AdminEndpoints_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/users?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record UserItem(Guid Id, string DisplayName, string? Email, bool IsAdmin, DateTimeOffset CreatedAt);
}
