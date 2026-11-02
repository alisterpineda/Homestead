using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Mcp;

// Swaps a validated MCP access token's principal for the Homestead one. Lookup-only and keyed on the OIDC subject
// (AppUser.ExternalSubject, recorded by every web sign-in): never creates a user, never touches role or display
// name — only the web callback provisions and re-syncs. The row is re-read per request, so deactivating or
// demoting someone in Homestead is immediate on this door too.
public static class McpBearerPrincipal
{
    public const string UnlinkedClaimType = AppUserPrincipal.UnlinkedClaimType;

    public static async Task ResolveAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var sub = context.Principal?.FindFirstValue("sub");
        var db = services.GetRequiredService<AppDbContext>();
        var user = string.IsNullOrWhiteSpace(sub)
            ? null
            : await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.ExternalSubject == sub, context.HttpContext.RequestAborted);

        if (user is null)
        {
            // Authenticated but unlinked: the McpActiveUser policy turns this into 403 account_not_linked. A 401
            // would make MCP clients restart OAuth in a loop, and the MCP challenge carries no description anyway.
            // Never log the email (not unique, user-editable, personal) or the raw subject.
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpBearerPrincipal));
            if (string.IsNullOrWhiteSpace(sub))
                logger.LogWarning("MCP bearer token without a sub claim rejected as unlinked");
            else
                logger.LogWarning("MCP bearer token for unknown subject {SubjectHash} rejected as unlinked", Hash(sub));

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(UnlinkedClaimType, "true")], JwtBearerDefaults.AuthenticationScheme));
            return;
        }

        context.Principal = AppUserPrincipal.Build(user, JwtBearerDefaults.AuthenticationScheme);
    }

    // A short, stable correlation handle for logs: the first 12 hex chars of SHA-256(sub).
    private static string Hash(string subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(subject)))[..12];
}
