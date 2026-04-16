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

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                Role = Enum.Parse<AppRole>(role),
                IsActive = isActive,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var loginResponse = await client.GetAsync($"/test/force-login?email={Uri.EscapeDataString(email)}");
        loginResponse.EnsureSuccessStatusCode();

        return client;
    }
}
