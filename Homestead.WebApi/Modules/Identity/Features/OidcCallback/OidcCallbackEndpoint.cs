using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Identity.Features.OidcLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Homestead.WebApi.Modules.Identity.Features.OidcCallback;

public static class OidcCallbackEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/callback", async (
            string? returnUrl,
            HttpContext ctx,
            OidcUserProvisioner provisioner,
            CancellationToken cancellationToken) =>
        {
            var authResult = await ctx.AuthenticateAsync(AuthenticationExtensions.ExternalOidcScheme);
            if (!authResult.Succeeded || authResult.Principal is null)
                return Results.Redirect("/login?error=oidc-failed");

            var user = await provisioner.ProvisionOrUpdateAsync(authResult.Principal, cancellationToken);

            // Always clean up the external bridge cookie, even when sign-in is blocked.
            await ctx.SignOutAsync(AuthenticationExtensions.ExternalOidcScheme);

            if (!user.IsActive)
                return Results.Redirect("/login?error=account-inactive");

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                AppUserPrincipal.Build(user),
                new AuthenticationProperties { IsPersistent = true });

            var redirect = OidcLoginEndpoint.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Redirect(redirect);
        })
        .WithName("OidcCallback")
        .ExcludeFromDescription()
        .AllowAnonymous();
    }
}
