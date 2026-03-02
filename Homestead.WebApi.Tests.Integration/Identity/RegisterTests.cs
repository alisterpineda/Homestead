using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Identity;

[Collection("Integration")]
public class RegisterTests : IntegrationTestBase
{
    public RegisterTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Register_FirstUser_Succeeds_And_IsAdmin()
    {
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "first@test.com",
            displayName = "First User",
            password = "Password123!"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var userInfoResponse = await client.GetAsync("/api/auth/user-info", CT);
        Assert.Equal(HttpStatusCode.OK, userInfoResponse.StatusCode);

        var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.Contains("Admin", userInfo!.Roles);
    }

    [Fact]
    public async Task Register_WhenUsersExist_ReturnsForbid()
    {
        await CreateAdminClientAsync();

        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "second@test.com",
            displayName = "Second User",
            password = "Password123!"
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidPassword_ReturnsValidationProblem()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "user@test.com",
            displayName = "User",
            password = "short"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUserInfo_Authenticated_ReturnsUserDetails()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.NotEmpty(body!.Email);
        Assert.NotEmpty(body.DisplayName);
        Assert.Contains("Admin", body.Roles);
    }

    [Fact]
    public async Task GetUserInfo_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsCookie()
    {
        var client = await CreateAdminClientAsync();

        var logoutResponse = await client.PostAsync("/api/auth/logout", null, CT);
        logoutResponse.EnsureSuccessStatusCode();

        var userInfoResponse = await client.GetAsync("/api/auth/user-info", CT);
        Assert.Equal(HttpStatusCode.Unauthorized, userInfoResponse.StatusCode);
    }

    private record UserInfoResult(Guid Id, string Email, string DisplayName, IList<string> Roles);
}
