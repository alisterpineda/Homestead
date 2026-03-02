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
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var userInfoResponse = await client.GetAsync("/api/auth/user-info");
        userInfoResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<UserInfoResult>();
        userInfo!.Roles.Should().Contain("Admin");
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
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUserInfo_Authenticated_ReturnsUserDetails()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/auth/user-info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>();
        body!.Email.Should().NotBeNullOrEmpty();
        body.DisplayName.Should().NotBeNullOrEmpty();
        body.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task GetUserInfo_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/auth/user-info");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ClearsCookie()
    {
        var client = await CreateAdminClientAsync();

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        logoutResponse.EnsureSuccessStatusCode();

        var userInfoResponse = await client.GetAsync("/api/auth/user-info");
        userInfoResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record UserInfoResult(Guid Id, string Email, string DisplayName, IList<string> Roles);
}
