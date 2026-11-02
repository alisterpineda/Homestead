using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
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

    /// <summary>
    /// Inserts an <see cref="AppUser"/> row directly (no sign-in). <paramref name="externalSubject"/> is the
    /// OIDC <c>sub</c> the bearer (MCP) door resolves users by; leave it null for a cookie-only user.
    /// </summary>
    public static async Task<AppUser> SeedUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string displayName,
        AppRole role,
        bool isActive,
        string? externalSubject = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser
        {
            Email = email,
            DisplayName = displayName,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            ExternalSubject = externalSubject,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
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

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        await SeedUserAsync(factory, email, displayName, Enum.Parse<AppRole>(role), isActive);

        var loginResponse = await client.GetAsync($"/test/force-login?email={Uri.EscapeDataString(email)}");
        loginResponse.EnsureSuccessStatusCode();

        return client;
    }
}
