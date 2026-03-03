using Homestead.WebApi.Domains.Identity;
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
        return CreateAuthenticatedClientAsync(factory, isAdmin: true, email, displayName, isActive);
    }

    public static Task<HttpClient> CreateUserClientAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        string? displayName = null,
        bool isActive = true)
    {
        return CreateAuthenticatedClientAsync(factory, isAdmin: false, email, displayName, isActive);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        bool isAdmin,
        string? email,
        string? displayName,
        bool isActive)
    {
        var counter = Interlocked.Increment(ref _userCounter);
        var prefix = isAdmin ? "admin" : "user";
        email ??= $"{prefix}{counter}@test.com";
        displayName ??= $"{(isAdmin ? "Admin" : "User")} {counter}";
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

        if (isAdmin)
            await userManager.AddToRoleAsync(user, "Admin");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();

        return client;
    }
}
