using System.Security.Claims;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Authentication;

public class OidcUserProvisioner(
    UserManager<AppUser> userManager,
    IOptions<OidcOptions> oidcOptions)
{
    private readonly OidcOptions _oidc = oidcOptions.Value;

    public async Task<AppUser> ProvisionOrUpdateAsync(ExternalLoginInfo loginInfo)
    {
        var providerKey = loginInfo.ProviderKey;
        var displayName = BuildDisplayName(loginInfo.Principal);
        var user = await userManager.FindByLoginAsync(loginInfo.LoginProvider, providerKey);

        if (user is null)
        {
            var email = loginInfo.Principal.FindFirstValue(ClaimTypes.Email)
                        ?? loginInfo.Principal.FindFirstValue("email");

            if (!string.IsNullOrEmpty(email))
                user = await userManager.FindByEmailAsync(email);

            if (user is null)
            {
                // Auto-activate the first user to bootstrap the system;
                // subsequent OIDC users require admin activation.
                var isFirstUser = !await userManager.Users.AnyAsync();

                user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = displayName,
                    IsActive = isFirstUser
                };

                var result = await userManager.CreateAsync(user);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            await userManager.AddLoginAsync(user, new UserLoginInfo(
                loginInfo.LoginProvider, providerKey, loginInfo.ProviderDisplayName));
        }

        if (!string.IsNullOrEmpty(displayName) && displayName != user.DisplayName)
        {
            user.DisplayName = displayName;
            await userManager.UpdateAsync(user);
        }

        await SyncAdminRoleAsync(user, loginInfo.Principal);

        return user;
    }

    private async Task SyncAdminRoleAsync(AppUser user, ClaimsPrincipal principal)
    {
        if (string.IsNullOrEmpty(_oidc.RoleClaimType) || string.IsNullOrEmpty(_oidc.AdminClaimValue))
            return;

        var hasAdminClaim = principal.FindAll(_oidc.RoleClaimType)
            .Any(c => string.Equals(c.Value, _oidc.AdminClaimValue, StringComparison.OrdinalIgnoreCase));
        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");

        if (hasAdminClaim && !isAdmin)
            await userManager.AddToRoleAsync(user, "Admin");
        else if (!hasAdminClaim && isAdmin)
            await userManager.RemoveFromRoleAsync(user, "Admin");
    }

    private static string BuildDisplayName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirstValue("name")
                   ?? principal.FindFirstValue(ClaimTypes.Name);

        if (!string.IsNullOrEmpty(name))
            return name;

        var given = principal.FindFirstValue("given_name")
                    ?? principal.FindFirstValue(ClaimTypes.GivenName) ?? "";
        var family = principal.FindFirstValue("family_name")
                     ?? principal.FindFirstValue(ClaimTypes.Surname) ?? "";

        var fullName = $"{given} {family}".Trim();
        return !string.IsNullOrEmpty(fullName)
            ? fullName
            : principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "";
    }
}
