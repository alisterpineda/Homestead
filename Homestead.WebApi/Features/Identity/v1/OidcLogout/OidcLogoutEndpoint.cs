using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Identity.v1.OidcLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.OidcLogout;

public static class OidcLogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/logout", async (string? returnUrl, HttpContext context) =>
        {
            var redirect = OidcLoginEndpoint.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            await context.SignOutAsync(OidcOptions.SchemeName,
                new AuthenticationProperties { RedirectUri = redirect });
        })
        .WithName("OidcLogout")
        .ExcludeFromDescription()
        .RequireAuthorization(AuthPolicies.AuthenticatedOnly);
    }
}
