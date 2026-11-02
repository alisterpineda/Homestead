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
            AdminClaimValues = ["admin", "platform-owner"]
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
    public async Task SubsequentOidcUser_WithAlternateAdminClaimValue_IsAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("existing@test.com", AppRole.Admin);

        var principal = CreatePrincipal("alt@test.com", "Alt Admin", "platform-owner");
        var user = await _sutWithRoleClaim.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(AppRole.Admin, user.Role);
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
        // With no RoleClaimType/AdminClaimValues configured, HasAdminClaim always returns false,
        // so an existing Admin with no admin claim on the principal gets demoted on each sign-in.
        // Deployments that want to preserve an Admin role across sign-ins must configure the
        // OIDC role claim on OidcOptions.
        var ct = TestContext.Current.CancellationToken;
        SeedExistingUser("admin@test.com", AppRole.Admin);

        var principal = CreatePrincipal("admin@test.com", "Admin");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(AppRole.Contributor, user.Role);
    }

    [Fact]
    public async Task NewUser_RecordsSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var principal = CreatePrincipal("new@test.com", "New User", subject: "sub-new");

        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        var stored = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, ct);
        Assert.Equal("sub-new", stored.ExternalSubject);
    }

    [Fact]
    public async Task NewUser_WithOnlyNameIdentifierClaim_RecordsSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var principal = CreatePrincipal("new@test.com", "New User");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, "sub-mapped"));

        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        var stored = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, ct);
        Assert.Equal("sub-mapped", stored.ExternalSubject);
    }

    [Fact]
    public async Task LegacyUser_MatchedByEmail_GainsSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var legacy = SeedExistingUser("legacy@test.com", AppRole.Contributor);

        var principal = CreatePrincipal("legacy@test.com", "Legacy", subject: "sub-legacy");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(legacy.Id, user.Id);
        var stored = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == legacy.Id, ct);
        Assert.Equal("sub-legacy", stored.ExternalSubject);
    }

    [Fact]
    // An email match must never re-link a row already bound to another subject: an IdP identity that sets its
    // email to a member's would otherwise take over that member's account on both doors.
    public async Task EmailMatchedUser_LinkedToADifferentSubject_IsRefusedAndLeftUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = SeedExistingUser("same@test.com", AppRole.Contributor, externalSubject: "sub-old");

        var principal = CreatePrincipal("same@test.com", "Same", subject: "sub-new");
        await Assert.ThrowsAsync<Homestead.WebApi.Common.AccessDeniedException>(
            () => _sut.ProvisionOrUpdateAsync(principal, ct));

        var stored = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == existing.Id, ct);
        Assert.Equal(("sub-old", 1), (stored.ExternalSubject, await _db.Users.CountAsync(ct)));
    }

    [Fact]
    public async Task SubjectMatch_WinsOverEmailChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = SeedExistingUser("old@test.com", AppRole.Contributor, externalSubject: "sub-stable");

        var principal = CreatePrincipal("changed@test.com", "Renamed", subject: "sub-stable");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(existing.Id, user.Id);
        Assert.Single(await _db.Users.ToListAsync(ct));
    }

    [Fact]
    public async Task SubjectMatch_WinsOverAnotherRowWithTheTokensEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var bySubject = SeedExistingUser("old@test.com", AppRole.Contributor, externalSubject: "sub-stable");
        SeedExistingUser("changed@test.com", AppRole.Contributor);

        var principal = CreatePrincipal("changed@test.com", "Renamed", subject: "sub-stable");
        var user = await _sut.ProvisionOrUpdateAsync(principal, ct);

        Assert.Equal(bySubject.Id, user.Id);
    }

    private AppUser SeedExistingUser(
        string email,
        AppRole role,
        bool isActive = true,
        string? displayName = null,
        string? externalSubject = null)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName ?? email,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            ExternalSubject = externalSubject,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private static ClaimsPrincipal CreatePrincipal(string email, string name, string? role = null, string? subject = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new("email", email),
            new("name", name),
        };

        if (subject is not null)
            claims.Add(new Claim("sub", subject));

        if (role is not null)
            claims.Add(new Claim("roles", role));

        var identity = new ClaimsIdentity(claims, "oidc");
        return new ClaimsPrincipal(identity);
    }
}
