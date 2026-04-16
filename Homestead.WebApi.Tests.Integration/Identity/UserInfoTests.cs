using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Identity;

[Collection("Integration")]
public class UserInfoTests : IntegrationTestBase
{
    public UserInfoTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetUserInfo_Authenticated_ReturnsUserDetails()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/auth/user-info", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        Assert.NotEmpty(body!.Email);
        Assert.NotEmpty(body.DisplayName);
        Assert.Equal("Admin", body.Role);
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
}
