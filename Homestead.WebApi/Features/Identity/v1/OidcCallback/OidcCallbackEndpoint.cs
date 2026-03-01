using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Features.Identity.v1.OidcLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.OidcCallback;

public static class OidcCallbackEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/callback", async (
            string? returnUrl,
            SignInManager<AppUser> signInManager,
            OidcUserProvisioner provisioner) =>
        {
            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.Redirect("/login?error=oidc-failed");

            var user = await provisioner.ProvisionOrUpdateAsync(info);
            await signInManager.SignInAsync(user, isPersistent: true);

            // Clear the external cookie
            await signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirect = OidcLoginEndpoint.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Redirect(redirect);
        })
        .WithName("OidcCallback")
        .ExcludeFromDescription()
        .AllowAnonymous();
    }
}
