using System.Security.Claims;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Authentication;

public class OidcUserProvisioner(
    AppDbContext db,
    IOptions<OidcOptions> oidcOptions)
{
    private readonly OidcOptions _oidc = oidcOptions.Value;

    public async Task<AppUser> ProvisionOrUpdateAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email")
                    ?? throw new InvalidOperationException("OIDC principal missing email claim.");
        var displayName = BuildDisplayName(principal);
        var hasAdminClaim = HasAdminClaim(principal);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Auto-activate the first user to bootstrap the system;
            // subsequent OIDC users require admin activation.
            var isFirstUser = !await db.Users.AnyAsync(ct);

            user = new AppUser
            {
                Email = email,
                DisplayName = displayName,
                IsActive = isFirstUser,
                CreatedAt = DateTimeOffset.UtcNow,
                Role = (isFirstUser || hasAdminClaim)
                    ? AppRole.Admin
                    : AppRole.Contributor,
            };
            db.Users.Add(user);
        }
        else
        {
            if (!string.IsNullOrEmpty(displayName) && displayName != user.DisplayName)
                user.DisplayName = displayName;

            // OIDC is source of truth for the Admin role.
            if (hasAdminClaim)
                user.Role = AppRole.Admin;
            else if (user.Role == AppRole.Admin)
                user.Role = AppRole.Contributor;
        }

        // The callback is a GET, so CommandTransactionFilter runs it with no transaction; save here.
        await db.SaveChangesAsync(ct);
        return user;
    }

    private bool HasAdminClaim(ClaimsPrincipal principal)
    {
        if (string.IsNullOrEmpty(_oidc.RoleClaimType) || _oidc.AdminClaimValues.Count == 0)
            return false;
        return principal.FindAll(_oidc.RoleClaimType)
            .Any(c => _oidc.AdminClaimValues.Contains(c.Value, StringComparer.OrdinalIgnoreCase));
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
