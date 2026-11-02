using System.Security.Claims;
using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Homestead.WebApi.Authentication;

public static class AppUserPrincipal
{
    public const string IsActiveClaimType = "IsActive";

    // Marks a principal whose token validated but whose subject matches no AppUser (the MCP bearer door). It has
    // no NameIdentifier, and ActiveUserHandler turns it into a 403 account_not_linked rather than a plain failure.
    public const string UnlinkedClaimType = "homestead:unlinked";

    public static ClaimsPrincipal Build(AppUser user) =>
        Build(user, CookieAuthenticationDefaults.AuthenticationScheme);

    // The one principal shape both doors produce (cookie sign-in, MCP bearer token), so ICurrentUserAccessor,
    // ActiveUserHandler, the role policies and the audit actor read the same claims either way.
    public static ClaimsPrincipal Build(AppUser user, string authenticationType)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(IsActiveClaimType, user.IsActive ? "true" : "false"),
        };
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, authenticationType));
    }
}
