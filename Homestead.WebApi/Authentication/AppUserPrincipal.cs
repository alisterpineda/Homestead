using System.Security.Claims;
using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Homestead.WebApi.Authentication;

public static class AppUserPrincipal
{
    public const string IsActiveClaimType = "IsActive";

    public static ClaimsPrincipal Build(AppUser user)
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
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
