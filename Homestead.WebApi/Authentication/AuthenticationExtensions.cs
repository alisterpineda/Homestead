using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Authentication;

public static class AuthenticationExtensions
{
    public const string ExternalOidcScheme = "ExternalOidc";
    public const string AuthCookieName = ".Homestead.Auth";
    public const string ExternalCookieName = ".Homestead.External";

    public static WebApplicationBuilder AddAppAuthentication(this WebApplicationBuilder builder)
    {
        var dataProtection = builder.Services.AddDataProtection();
        if (BuildTimeOpenApi.IsGenerating)
        {
            // The build-time OpenAPI generator has no database for the key ring, and nothing is
            // protected during a throwaway spec-gen host run. Use an in-memory provider (so the DB
            // key ring is never read) and quiet the framework's expected "keys are not persisted"
            // notice so the build stays clean. Both are scoped to this run only.
            dataProtection.UseEphemeralDataProtectionProvider();
            builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        }
        else
        {
            dataProtection.PersistKeysToDbContext<AppDbContext>();
        }

        builder.Services.AddHttpContextAccessor();

        var isDev = builder.Environment.IsDevelopment();

        var oidcSection = builder.Configuration.GetSection(OidcOptions.SectionName);
        var oidcOptions = oidcSection.Get<OidcOptions>() ?? new OidcOptions();
        builder.Services.Configure<OidcOptions>(oidcSection);

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = isDev ? SameSiteMode.Lax : SameSiteMode.Strict;
                // In development the Angular proxy forwards over HTTPS but the browser
                // is on HTTP, so the Secure flag would cause the cookie to be rejected.
                options.Cookie.SecurePolicy = isDev
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);

                // SPA: return 401/403 instead of redirecting to a login page
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddCookie(ExternalOidcScheme, options =>
            {
                // Short-lived bridge cookie the OIDC handler writes to; the callback
                // reads it, provisions the user, then swaps it for the main app cookie.
                options.Cookie.Name = ExternalCookieName;
                options.Cookie.HttpOnly = true;
                // Must be Lax so the cookie survives the cross-site redirect back from the IdP.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = isDev
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddOpenIdConnect(OidcOptions.SchemeName, options =>
            {
                options.Authority = oidcOptions.Authority;
                if (!string.IsNullOrEmpty(oidcOptions.MetadataAddress))
                    options.MetadataAddress = oidcOptions.MetadataAddress;
                options.ClientId = oidcOptions.ClientId;
                options.ClientSecret = oidcOptions.ClientSecret;
                options.RequireHttpsMetadata = !isDev;
                options.ResponseType = "code";
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                foreach (var scope in oidcOptions.AdditionalScopes)
                    options.Scope.Add(scope);
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.SignInScheme = ExternalOidcScheme;
                options.SignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    // Keycloak requires client_id or id_token_hint with post_logout_redirect_uri.
                    // Since we don't save tokens, send client_id explicitly.
                    context.ProtocolMessage.ClientId = oidcOptions.ClientId;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Authorization.DocumentAuthorizationHandler>();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.AuthenticatedOnly, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(AuthPolicies.ActiveUser, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveUserRequirement());
            });

            options.AddPolicy(AuthPolicies.RequireAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveUserRequirement());
                policy.RequireRole(nameof(AppRole.Admin));
            });

            options.AddPolicy(AuthPolicies.RequireContributor, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveUserRequirement());
                policy.RequireRole(nameof(AppRole.Admin), nameof(AppRole.Contributor));
            });

            options.AddPolicy(AuthPolicies.RequireViewer, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveUserRequirement());
                policy.RequireRole(nameof(AppRole.Admin), nameof(AppRole.Contributor), nameof(AppRole.Viewer));
            });

            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveUserRequirement())
                .Build();
        });

        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler>(sp =>
            new InactiveUserAuthorizationMiddlewareResultHandler(
                new AuthorizationMiddlewareResultHandler()));

        builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        builder.Services.AddScoped<OidcUserProvisioner>();

        return builder;
    }
}
