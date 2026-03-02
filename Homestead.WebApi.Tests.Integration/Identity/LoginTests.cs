using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        result.Succeeded.Should().BeTrue();
    }
}
