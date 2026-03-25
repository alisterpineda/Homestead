using System.Security.Claims;
using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Authentication;

public class AppClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("IsActive", user.IsActive.ToString().ToLowerInvariant()));
        return identity;
    }
}
