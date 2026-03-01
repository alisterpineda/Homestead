using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Homestead.WebApi.Features.Identity.v1.OidcLogin;

public static class OidcLoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/login", (string? returnUrl) =>
        {
            var redirectUrl = IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
            var properties = new AuthenticationProperties
            {
                RedirectUri = $"/api/auth/oidc/callback?returnUrl={Uri.EscapeDataString(redirectUrl)}",
                Items = { ["LoginProvider"] = OidcOptions.SchemeName }
            };

            return Results.Challenge(properties, [OidcOptions.SchemeName]);
        })
        .WithName("OidcLogin")
        .Produces(StatusCodes.Status302Found)
        .AllowAnonymous();
    }

    internal static bool IsSafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // Local path
        if (url.StartsWith('/') && !url.StartsWith("//"))
            return true;

        // Full URL — only allow http(s) to localhost (for dev proxy)
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" && uri.Host == "localhost";

        return false;
    }
}
