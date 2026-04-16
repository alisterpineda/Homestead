using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Identity.Features.OidcLogin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Modules.Identity.Features.OidcCallback;

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

            // Always clean up the external cookie, even when sign-in is blocked
            await signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);

            if (!user.IsActive)
                return Results.Redirect("/login?error=account-inactive");

            await signInManager.SignInAsync(user, isPersistent: true);
            var redirect = OidcLoginEndpoint.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Redirect(redirect);
        })
        .WithName("OidcCallback")
        .ExcludeFromDescription()
        .AllowAnonymous();
    }
}
