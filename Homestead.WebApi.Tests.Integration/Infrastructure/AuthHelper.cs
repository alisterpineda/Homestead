using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

public static class AuthHelper
{
    private static int _userCounter;

    public static Task<HttpClient> CreateAdminClientAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        string? displayName = null,
        bool isActive = true)
    {
        return CreateAuthenticatedClientAsync(factory, role: "Admin", email, displayName, isActive);
    }

    public static Task<HttpClient> CreateUserClientAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        string? displayName = null,
        bool isActive = true,
        string role = "Contributor")
    {
        return CreateAuthenticatedClientAsync(factory, role: role, email, displayName, isActive);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string role,
        string? email,
        string? displayName,
        bool isActive)
    {
        var counter = Interlocked.Increment(ref _userCounter);
        var prefix = role.ToLowerInvariant();
        email ??= $"{prefix}{counter}@test.com";
        displayName ??= $"{role} {counter}";
        const string password = "TestPassword123!";

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true,
            IsActive = isActive
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new Exception($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, role);

        if (isActive)
        {
            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            loginResponse.EnsureSuccessStatusCode();
        }
        else
        {
            // Inactive users can't login via the endpoint, so use the force-login test endpoint
            // which bypasses the IsActive check by calling SignInManager.SignInAsync directly
            var loginResponse = await client.GetAsync($"/test/force-login?email={Uri.EscapeDataString(email)}");
            loginResponse.EnsureSuccessStatusCode();
        }

        return client;
    }
}
