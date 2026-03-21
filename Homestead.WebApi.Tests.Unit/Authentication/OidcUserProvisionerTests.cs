using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Unit.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Authentication;

public class OidcUserProvisionerTests : IDisposable
{
    private readonly UserManager<AppUser> _userManager;
    private readonly TestAppDbContext _db;
    private readonly IRoleSourceManager _roleSourceManager;
    private readonly OidcUserProvisioner _sut;
    private readonly OidcUserProvisioner _sutWithRoleClaim;

    public OidcUserProvisionerTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(
            store, null, null, null, null, null, null, null, null);

        _db = TestAppDbContext.Create();

        // Seed the Admin role
        var adminRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN" };
        var contributorRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Contributor", NormalizedName = "CONTRIBUTOR" };
        _db.Roles.AddRange(adminRole, contributorRole);
        _db.SaveChanges();

        _roleSourceManager = new RoleSourceManager(_db);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://idp.example.com",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        });

        _sut = new OidcUserProvisioner(_userManager, _roleSourceManager, oidcOptions, _db);

        var oidcOptionsWithRoleClaim = Options.Create(new OidcOptions
        {
            Authority = "https://idp.example.com",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RoleClaimType = "roles",
            AdminClaimValue = "admin"
        });

        _sutWithRoleClaim = new OidcUserProvisioner(_userManager, _roleSourceManager, oidcOptionsWithRoleClaim, _db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task FirstOidcUser_IsCreatedWithIsActiveTrue()
    {
        _userManager.FindByLoginAsync("oidc-provider", "first-key")
            .Returns((AppUser?)null);
        _userManager.FindByEmailAsync("first@test.com")
            .Returns((AppUser?)null);
        _userManager.Users.Returns(Array.Empty<AppUser>().AsAsyncQueryable());
        _userManager.CreateAsync(Arg.Any<AppUser>())
            .Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<AppUser>(), Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.GetRolesAsync(Arg.Any<AppUser>()).Returns(new List<string> { "Admin" });
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("first@test.com", "First User");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "first-key", "OIDC")
        {
            Principal = principal
        };

        await _sut.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.Received(1).CreateAsync(
            Arg.Is<AppUser>(u => u.IsActive == true && u.Email == "first@test.com"));
    }

    [Fact]
    public async Task SubsequentOidcUser_IsCreatedWithIsActiveFalse()
    {
        var existingUsers = new[] { new AppUser { Id = Guid.NewGuid() } };
        _userManager.FindByLoginAsync("oidc-provider", "provider-key-1")
            .Returns((AppUser?)null);
        _userManager.FindByEmailAsync("newoidc@test.com")
            .Returns((AppUser?)null);
        _userManager.Users.Returns(existingUsers.AsAsyncQueryable());
        _userManager.CreateAsync(Arg.Any<AppUser>())
            .Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<AppUser>(), Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.GetRolesAsync(Arg.Any<AppUser>()).Returns(new List<string>());
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("newoidc@test.com", "New OIDC User");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "provider-key-1", "OIDC")
        {
            Principal = principal
        };

        await _sut.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.Received(1).CreateAsync(
            Arg.Is<AppUser>(u => u.IsActive == false && u.Email == "newoidc@test.com"));
    }

    [Fact]
    public async Task ExistingOidcUser_RetainsIsActiveTrue()
    {
        var existingUser = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "existing@test.com",
            Email = "existing@test.com",
            DisplayName = "Existing User",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "existing-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string>());
        _userManager.AddToRoleAsync(existingUser, Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("existing@test.com", "Existing User");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "existing-key", "OIDC")
        {
            Principal = principal
        };

        var user = await _sut.ProvisionOrUpdateAsync(loginInfo);

        Assert.True(user.IsActive);
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<AppUser>());
    }

    [Fact]
    public async Task ExistingInactiveOidcUser_StaysInactive()
    {
        var existingUser = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "inactive@test.com",
            Email = "inactive@test.com",
            DisplayName = "Inactive User",
            IsActive = false
        };

        _userManager.FindByLoginAsync("oidc-provider", "inactive-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string>());
        _userManager.AddToRoleAsync(existingUser, Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("inactive@test.com", "Inactive User");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "inactive-key", "OIDC")
        {
            Principal = principal
        };

        var user = await _sut.ProvisionOrUpdateAsync(loginInfo);

        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task NewOidcUser_LinkedByEmail_RetainsExistingIsActive()
    {
        var existingUser = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "emailmatch@test.com",
            Email = "emailmatch@test.com",
            DisplayName = "Email Match",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "new-provider-key")
            .Returns((AppUser?)null);
        _userManager.FindByEmailAsync("emailmatch@test.com")
            .Returns(existingUser);
        _userManager.AddLoginAsync(existingUser, Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string>());
        _userManager.AddToRoleAsync(existingUser, Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("emailmatch@test.com", "Email Match Updated");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "new-provider-key", "OIDC")
        {
            Principal = principal
        };

        var user = await _sut.ProvisionOrUpdateAsync(loginInfo);

        Assert.True(user.IsActive);
        Assert.Equal(existingUser.Id, user.Id);
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<AppUser>());
    }

    [Fact]
    public async Task NewOidcUser_GetsContributorRole()
    {
        _userManager.FindByLoginAsync("oidc-provider", "role-key")
            .Returns((AppUser?)null);
        _userManager.FindByEmailAsync("role@test.com")
            .Returns((AppUser?)null);
        var existingUsers = new[] { new AppUser { Id = Guid.NewGuid() } };
        _userManager.Users.Returns(existingUsers.AsAsyncQueryable());
        _userManager.CreateAsync(Arg.Any<AppUser>())
            .Returns(IdentityResult.Success);
        _userManager.AddLoginAsync(Arg.Any<AppUser>(), Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.GetRolesAsync(Arg.Any<AppUser>()).Returns(new List<string>());
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var principal = CreatePrincipal("role@test.com", "Role Test");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "role-key", "OIDC")
        {
            Principal = principal
        };

        await _sut.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.Received(1).AddToRoleAsync(Arg.Any<AppUser>(), "Contributor");
    }

    [Fact]
    public async Task ExistingOidcUser_WithRole_DoesNotGetDuplicate()
    {
        var existingUser = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "hasrole@test.com",
            Email = "hasrole@test.com",
            DisplayName = "Has Role",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "hasrole-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string> { "Contributor" });

        var principal = CreatePrincipal("hasrole@test.com", "Has Role");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "hasrole-key", "OIDC")
        {
            Principal = principal
        };

        await _sut.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.DidNotReceive().AddToRoleAsync(existingUser, Arg.Any<string>());
    }

    [Fact]
    public async Task FirstOidcUser_GetsAdminRoleWithLocalSource()
    {
        var userId = Guid.NewGuid();
        var adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin");

        _userManager.FindByLoginAsync("oidc-provider", "first-key")
            .Returns((AppUser?)null);
        _userManager.FindByEmailAsync("first@test.com")
            .Returns((AppUser?)null);
        _userManager.Users.Returns(Array.Empty<AppUser>().AsAsyncQueryable());
        _userManager.CreateAsync(Arg.Any<AppUser>())
            .Returns(ci =>
            {
                var u = ci.Arg<AppUser>();
                u.Id = userId;
                return IdentityResult.Success;
            });
        _userManager.AddLoginAsync(Arg.Any<AppUser>(), Arg.Any<UserLoginInfo>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), "Admin")
            .Returns(ci =>
            {
                // Simulate what UserManager does internally
                _db.Set<AppUserRole>().Add(new AppUserRole
                {
                    UserId = userId,
                    RoleId = adminRole.Id
                });
                _db.SaveChanges();
                return IdentityResult.Success;
            });
        _userManager.GetRolesAsync(Arg.Any<AppUser>()).Returns(new List<string> { "Admin" });

        var principal = CreatePrincipal("first@test.com", "First User");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "first-key", "OIDC")
        {
            Principal = principal
        };

        await _sut.ProvisionOrUpdateAsync(loginInfo);

        var userRole = await _db.Set<AppUserRole>()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id);
        Assert.NotNull(userRole);
        Assert.Equal(RoleSource.Local, userRole.Source);
    }

    [Fact]
    public async Task OidcClaimGrantsAdmin_SetsOidcSource()
    {
        var userId = Guid.NewGuid();
        var adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin");
        var existingUser = new AppUser
        {
            Id = userId,
            UserName = "claimuser@test.com",
            Email = "claimuser@test.com",
            DisplayName = "Claim User",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "claim-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string> { "Contributor" });
        _userManager.AddToRoleAsync(existingUser, "Admin")
            .Returns(ci =>
            {
                _db.Set<AppUserRole>().Add(new AppUserRole
                {
                    UserId = userId,
                    RoleId = adminRole.Id
                });
                _db.SaveChanges();
                return IdentityResult.Success;
            });

        var principal = CreatePrincipal("claimuser@test.com", "Claim User", "admin");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "claim-key", "OIDC")
        {
            Principal = principal
        };

        await _sutWithRoleClaim.ProvisionOrUpdateAsync(loginInfo);

        var userRole = await _db.Set<AppUserRole>()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id);
        Assert.NotNull(userRole);
        Assert.Equal(RoleSource.Oidc, userRole.Source);
    }

    [Fact]
    public async Task OidcClaimRemoved_RevokesAdminWhenSourceIsOidc()
    {
        var userId = Guid.NewGuid();
        var adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin");

        // Pre-seed an OIDC-sourced Admin role
        _db.Set<AppUserRole>().Add(new AppUserRole
        {
            UserId = userId,
            RoleId = adminRole.Id,
            Source = RoleSource.Oidc
        });
        await _db.SaveChangesAsync();

        var existingUser = new AppUser
        {
            Id = userId,
            UserName = "revokeoidc@test.com",
            Email = "revokeoidc@test.com",
            DisplayName = "Revoke OIDC",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "revoke-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string> { "Admin", "Contributor" });
        _userManager.RemoveFromRoleAsync(existingUser, "Admin")
            .Returns(IdentityResult.Success);

        // No admin claim in the principal
        var principal = CreatePrincipal("revokeoidc@test.com", "Revoke OIDC", "user");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "revoke-key", "OIDC")
        {
            Principal = principal
        };

        await _sutWithRoleClaim.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.Received(1).RemoveFromRoleAsync(existingUser, "Admin");
    }

    [Fact]
    public async Task OidcClaimRemoved_PreservesAdminWhenSourceIsLocal()
    {
        var userId = Guid.NewGuid();
        var adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin");

        // Pre-seed a locally-sourced Admin role
        _db.Set<AppUserRole>().Add(new AppUserRole
        {
            UserId = userId,
            RoleId = adminRole.Id,
            Source = RoleSource.Local
        });
        await _db.SaveChangesAsync();

        var existingUser = new AppUser
        {
            Id = userId,
            UserName = "keeplocal@test.com",
            Email = "keeplocal@test.com",
            DisplayName = "Keep Local",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "keep-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string> { "Admin", "Contributor" });

        // No admin claim in the principal
        var principal = CreatePrincipal("keeplocal@test.com", "Keep Local", "user");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "keep-key", "OIDC")
        {
            Principal = principal
        };

        await _sutWithRoleClaim.ProvisionOrUpdateAsync(loginInfo);

        await _userManager.DidNotReceive().RemoveFromRoleAsync(existingUser, "Admin");
    }

    [Fact]
    public async Task OidcClaimPresent_DoesNotOverrideLocalSource()
    {
        var userId = Guid.NewGuid();
        var adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin");

        // Pre-seed a locally-sourced Admin role
        _db.Set<AppUserRole>().Add(new AppUserRole
        {
            UserId = userId,
            RoleId = adminRole.Id,
            Source = RoleSource.Local
        });
        await _db.SaveChangesAsync();

        var existingUser = new AppUser
        {
            Id = userId,
            UserName = "localadmin@test.com",
            Email = "localadmin@test.com",
            DisplayName = "Local Admin",
            IsActive = true
        };

        _userManager.FindByLoginAsync("oidc-provider", "localadmin-key")
            .Returns(existingUser);
        _userManager.GetRolesAsync(existingUser).Returns(new List<string> { "Admin", "Contributor" });

        // OIDC claim also says admin — source should remain Local
        var principal = CreatePrincipal("localadmin@test.com", "Local Admin", "admin");
        var loginInfo = new ExternalLoginInfo(principal, "oidc-provider", "localadmin-key", "OIDC")
        {
            Principal = principal
        };

        await _sutWithRoleClaim.ProvisionOrUpdateAsync(loginInfo);

        var userRole = await _db.Set<AppUserRole>()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id);
        Assert.NotNull(userRole);
        Assert.Equal(RoleSource.Local, userRole.Source);
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
