using System.Security.Claims;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// Mints IdP-shaped access tokens for the MCP bearer door and seeds the users they resolve to. Tokens are signed
/// with <see cref="HomesteadApiFactory.McpSigningKey"/> (or the trusted HMAC key for <c>HS256</c>), the key the
/// factory pins the JwtBearer configuration to.
/// </summary>
public static class McpAuthHelper
{
    private static int _userCounter;

    /// <summary>
    /// A signed access token for <paramref name="subject"/>. The validity window always ends at now +
    /// <paramref name="lifetime"/> and starts ten minutes before that, so a negative lifetime yields an expired
    /// token that is otherwise well-formed (nbf &lt; exp). <paramref name="audience"/> defaults to
    /// <see cref="HomesteadApiFactory.McpTestAudience"/>, <paramref name="issuer"/> to
    /// <see cref="HomesteadApiFactory.McpTestIssuer"/>.
    /// </summary>
    public static string CreateToken(
        string? subject,
        string? email,
        TimeSpan? lifetime = null,
        string? audience = null,
        string alg = SecurityAlgorithms.RsaSha256,
        string? name = null,
        string? issuer = null)
    {
        var expires = DateTime.UtcNow + (lifetime ?? TimeSpan.FromMinutes(5));
        var claims = new Dictionary<string, object>();
        if (subject is not null) claims["sub"] = subject;
        if (email is not null) claims["email"] = email;
        if (name is not null) claims["name"] = name;

        SecurityKey key = alg switch
        {
            SecurityAlgorithms.RsaSha256 => HomesteadApiFactory.McpSigningKey,
            SecurityAlgorithms.HmacSha256 => HomesteadApiFactory.McpSymmetricKey,
            _ => throw new ArgumentOutOfRangeException(nameof(alg), alg, "Only RS256 and HS256 are minted."),
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? HomesteadApiFactory.McpTestIssuer,
            Audience = audience ?? HomesteadApiFactory.McpTestAudience,
            Claims = claims,
            Expires = expires,
            NotBefore = expires - TimeSpan.FromMinutes(10),
            IssuedAt = expires - TimeSpan.FromMinutes(10),
            SigningCredentials = new SigningCredentials(key, alg),
        });
    }

    /// <summary>
    /// Seeds a user linked to a fresh OIDC subject (as a prior web sign-in would have) and returns it with a valid
    /// access token for that subject.
    /// </summary>
    public static async Task<(AppUser User, string Token)> SeedAsync(
        WebApplicationFactory<Program> factory,
        AppRole role,
        bool isActive = true,
        string? email = null,
        string? displayName = null)
    {
        var counter = Interlocked.Increment(ref _userCounter);
        email ??= $"mcp-{role.ToString().ToLowerInvariant()}{counter}@test.com";
        displayName ??= $"MCP {role} {counter}";
        var subject = $"mcp-subject-{Guid.NewGuid():N}";

        var user = await AuthHelper.SeedUserAsync(factory, email, displayName, role, isActive, subject);
        return (user, CreateToken(subject, email));
    }
}
