using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Tests.Unit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Unit.Authentication;

public class OidcUserProvisionerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly OidcUserProvisioner _sut;
    private readonly OidcUserProvisioner _sutWithRoleClaim;

    public OidcUserProvisionerTests()
    {
        _db = TestAppDbContext.Create();

        _sut = new OidcUserProvisioner(_db, Options.Create(new OidcOptions
        {
            Authority = "https://idp.example.com",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        }));

        _sutWithRoleClaim = new OidcUserProvisioner(_db, Options.Create(new OidcOptions
        {
            Authority = "https://idp.example.com",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RoleClaimType = "roles",
            AdminClaimValue = "admin"
        }));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FirstOidcUser_IsCreatedActiveAsAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var principal = CreatePrincipal("first@test.com", "First User");

        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.True(user.IsActive);
        Assert.Equal(AppRole.Admin, user.Role);
        Assert.Equal("first@test.com", user.Email);
        Assert.Equal("First User", user.DisplayName);
        Assert.Single(await _db.Users.ToListAsync(ct));
    }

    [Fact]
    public async Task SubsequentOidcUser_NoAdminClaim_IsInactiveContributor()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("existing@test.com", AppRole.Admin);

        var principal = CreatePrincipal("newoidc@test.com", "New OIDC User");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.False(user.IsActive);
        Assert.Equal(AppRole.Contributor, user.Role);
    }

    [Fact]
    public async Task SubsequentOidcUser_WithAdminClaim_IsInactiveAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("existing@test.com", AppRole.Admin);

        var principal = CreatePrincipal("newoidc@test.com", "New OIDC User", "admin");
        var user = await _sutWithRoleClaim.ProvisionOrUpdateAsync(principal, ct);

        Assert.False(user.IsActive);
        Assert.Equal(AppRole.Admin, user.Role);
    }

    [Fact]
    public async Task ExistingUser_LinkedByEmail_UpdatesDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = SeedExistingUser("match@test.com", AppRole.Contributor, displayName: "Old Name");

        var principal = CreatePrincipal("match@test.com", "New Name");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(existing.Id, user.Id);
        Assert.Equal("New Name", user.DisplayName);
        Assert.Single(await _db.Users.ToListAsync(ct));
    }

    [Fact]
    public async Task ExistingUser_GainsAdminClaim_PromotedToAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("promote@test.com", AppRole.Contributor);

        var principal = CreatePrincipal("promote@test.com", "Promote Me", "admin");
        var user = await _sutWithRoleClaim.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(AppRole.Admin, user.Role);
    }

    [Fact]
    public async Task ExistingAdmin_LosesAdminClaim_DemotedToContributor()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("demote@test.com", AppRole.Admin);

        var principal = CreatePrincipal("demote@test.com", "Demote Me", "user");
        var user = await _sutWithRoleClaim.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(AppRole.Contributor, user.Role);
    }

    [Fact]
    public async Task InactiveUser_PreservesIsActiveFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("inactive@test.com", AppRole.Contributor, isActive: false);

        var principal = CreatePrincipal("inactive@test.com", "Inactive");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task RoleClaimNotConfigured_DemotesAdminToDefault()
    {
        // With no RoleClaimType/AdminClaimValue configured, HasAdminClaim always returns false,
        // so an existing Admin with no admin claim on the principal gets demoted on each sign-in.
        // Deployments that want to preserve an Admin role across sign-ins must configure the
        // OIDC role claim on OidcOptions.
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("admin@test.com", AppRole.Admin);

        var principal = CreatePrincipal("admin@test.com", "Admin");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(AppRole.Contributor, user.Role);
    }

    private AppUser SeedExistingUser(string email, AppRole role, bool isActive = true, string? displayName = null)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName ?? email,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private static ClaimsPrincipal CreatePrincipal(string email, string name, string? role = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new("email", email),
            new("name", name),
        };

        if (role is not null)
            claims.Add(new Claim("roles", role));

        var identity = new ClaimsIdentity(claims, "oidc");
        return new ClaimsPrincipal(identity);
    }
}
