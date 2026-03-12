using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Tests.Integration.Identity;

[Collection("Integration")]
public class LoginTests : IntegrationTestBase
{
    public LoginTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        const string email = "login-valid@test.com";
        const string password = "Password123!";
        await SeedUserAsync(email, password);

        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        const string email = "login-invalid@test.com";
        await SeedUserAsync(email, "Password123!");

        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPassword!"
        }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonexistentUser_Returns401()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "doesnotexist@test.com",
            password = "Password123!"
        }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ExceedsMaxAttempts_ReturnsLockedOut()
    {
        const string email = "lockout@test.com";
        const string password = "Password123!";
        await SeedUserAsync(email, password);

        var client = CreateAnonymousClient();
        await MakeFailedLoginAttemptsAsync(client, email, 5);

        // 6th attempt should return account_locked
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword!" }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(CT);
        Assert.Equal("account_locked", body?.Error);
    }

    [Fact]
    public async Task Login_LockedOut_ValidCredentialsStillFail()
    {
        const string email = "lockout-valid@test.com";
        const string password = "Password123!";
        await SeedUserAsync(email, password);

        var client = CreateAnonymousClient();
        await MakeFailedLoginAttemptsAsync(client, email, 5);

        // Attempt with correct password should still fail with account_locked
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(CT);
        Assert.Equal("account_locked", body?.Error);
    }

    [Fact]
    public async Task Login_SuccessResetsFailedCount()
    {
        const string email = "lockout-reset@test.com";
        const string password = "Password123!";
        await SeedUserAsync(email, password);

        var client = CreateAnonymousClient();

        // Make 4 failed attempts (below threshold of 5)
        await MakeFailedLoginAttemptsAsync(client, email, 4);

        // Successful login should reset the counter
        var successResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, CT);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        // Make 4 more failed attempts — should NOT trigger lockout since counter was reset
        await MakeFailedLoginAttemptsAsync(client, email, 4);

        // 5th attempt after reset should still not be locked out (only 4 failures)
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedUserAsync(string email, string password)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = "Test User",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded);
    }

    private static async Task MakeFailedLoginAttemptsAsync(HttpClient client, string email, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword!" }, CT);
        }
    }
}
