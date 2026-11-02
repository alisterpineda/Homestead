using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace Homestead.WebApi.Modules.Mcp;

// The MCP door's authentication: a JWT "Bearer" scheme that validates IdP-issued access tokens locally, plus the
// SDK's "McpAuth" scheme, which forwards authenticate to Bearer, issues the RFC 9728 challenge
// (WWW-Authenticate: Bearer resource_metadata="…") and serves /.well-known/oauth-protected-resource/mcp. The cookie
// door is untouched: parameterless AddAuthentication() adds schemes without changing the Cookies defaults, and only
// the McpActiveUser policy names McpAuth, so a bearer token is ignored everywhere else.
internal static class McpAuthenticationExtensions
{
    // Authentik with no signing key falls back to HS256 keyed with the client secret — which Claude holds — so only
    // asymmetric algorithms are accepted.
    private static readonly string[] AllowedAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256];

    public static void AddMcpAuthentication(this IServiceCollection services, McpOptions options, bool isDevelopment)
    {
        services.AddAuthentication()
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Authority = options.Authority;
                if (!string.IsNullOrEmpty(options.MetadataAddress))
                    o.MetadataAddress = options.MetadataAddress;
                o.RequireHttpsMetadata = !isDevelopment;
                o.MapInboundClaims = false;
                o.SaveToken = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = AllowedAlgorithms,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = McpBearerPrincipal.ResolveAsync };
            })
            .AddMcp(o => o.ResourceMetadata = new ProtectedResourceMetadata
            {
                // Verbatim (clients reject a resource that is not byte-identical to the URL the user typed). Null →
                // inferred from the request (Development only; Validate requires PublicUrl elsewhere). Never set
                // ResourceMetadataUri: pointing it at another host makes the metadata route 404.
                Resource = string.IsNullOrWhiteSpace(options.PublicUrl) ? null : options.PublicUrl,
                // Verbatim: clients compare it byte-for-byte with the IdP's issuer (Authentik's ends in '/').
                AuthorizationServers = [options.Authority!],
                ScopesSupported = McpConfiguration.ResolveScopes(options).ToList(),
            });

        services.AddAuthorizationBuilder().AddPolicy(AuthPolicies.McpActiveUser, p =>
        {
            p.AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme);
            p.RequireAuthenticatedUser();
            p.AddRequirements(new ActiveUserRequirement());
        });
    }
}
