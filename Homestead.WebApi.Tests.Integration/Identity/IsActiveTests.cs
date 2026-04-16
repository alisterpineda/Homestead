using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Identity;

[Collection("Integration")]
public class IsActiveTests : IntegrationTestBase
{
    public IsActiveTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ActiveUser_CanAccessDocuments()
    {
        var client = await CreateUserClientAsync(isActive: true);

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InactiveUser_Gets403OnDocuments()
    {
        var client = await CreateUserClientAsync(isActive: false);

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Equal("account_inactive", body!.ErrorCode);
    }

    [Fact]
    public async Task InactiveUser_CanAccessUserInfo()
    {
        var client = await CreateUserClientAsync(isActive: false);

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.False(body!.IsActive);
    }

    [Fact]
    public async Task AdminCanActivateUser()
    {
        var adminClient = await CreateAdminClientAsync();

        // Create a user via admin API
        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "activate-target@test.com",
            displayName = "Activate Target",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        // Deactivate the user
        await adminClient.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}", new
        {
            email = "activate-target@test.com",
            displayName = "Activate Target",
            role = "Contributor",
            isActive = false
        }, CT);

        // Verify the user is inactive
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == "activate-target@test.com", CT);
        Assert.False(user.IsActive);

        // Re-activate the user
        var activateResponse = await adminClient.PutAsJsonAsync($"/api/v1/admin/users/{created.Id}", new
        {
            email = "activate-target@test.com",
            displayName = "Activate Target",
            role = "Contributor",
            isActive = true
        }, CT);
        activateResponse.EnsureSuccessStatusCode();

        // Verify re-activation
        using var scope2 = CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var reactivatedUser = await db2.Users.FirstAsync(u => u.Email == "activate-target@test.com", CT);
        Assert.True(reactivatedUser.IsActive);
    }

    [Fact]
    public async Task AdminCanDeactivateUser()
    {
        var adminClient = await CreateAdminClientAsync();

        // Create a user via admin API (active by default)
        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email = "deactivate-target@test.com",
            displayName = "Deactivate Target",
            role = "Contributor"
        }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UserItem>(CT);

        // Deactivate the user
        var deactivateResponse = await adminClient.PutAsJsonAsync($"/api/v1/admin/users/{created!.Id}", new
        {
            email = "deactivate-target@test.com",
            displayName = "Deactivate Target",
            role = "Contributor",
            isActive = false
        }, CT);
        deactivateResponse.EnsureSuccessStatusCode();
        var body = await deactivateResponse.Content.ReadFromJsonAsync<UserItem>(CT);
        Assert.False(body!.IsActive);
    }
}
