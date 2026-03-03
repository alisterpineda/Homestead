using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Homestead.WebApi.Tests.Unit.Infrastructure;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Authentication;

public class OidcUserProvisionerTests
{
    private readonly UserManager<AppUser> _userManager;
    private readonly OidcUserProvisioner _sut;

    public OidcUserProvisionerTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(
            store, null, null, null, null, null, null, null, null);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://idp.example.com",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        });

        _sut = new OidcUserProvisioner(_userManager, oidcOptions);
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

    private static ClaimsPrincipal CreatePrincipal(string email, string name)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim("name", name),
        };
        var identity = new ClaimsIdentity(claims, "oidc");
        return new ClaimsPrincipal(identity);
    }
}
