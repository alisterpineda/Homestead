using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Identity.Features.OidcLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Homestead.WebApi.Modules.Identity.Features.OidcLogout;

public static class OidcLogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/logout", async (string? returnUrl, HttpContext context) =>
        {
            var redirect = OidcLoginEndpoint.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(OidcOptions.SchemeName,
                new AuthenticationProperties { RedirectUri = redirect });
        })
        .WithName("OidcLogout")
        .ExcludeFromDescription()
        .RequireAuthorization(AuthPolicies.AuthenticatedOnly);
    }
}
