using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Authentication;

public static class AuthenticationExtensions
{
    public static WebApplicationBuilder AddAppAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddIdentityApiEndpoints<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

        var isDev = builder.Environment.IsDevelopment();

        builder.Services.ConfigureApplicationCookie(options =>
        {
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

        // Conditionally add OIDC authentication
        var oidcSection = builder.Configuration.GetSection(OidcOptions.SectionName);
        var oidcOptions = oidcSection.Get<OidcOptions>();

        builder.Services.Configure<OidcOptions>(oidcSection);

        if (oidcOptions?.IsConfigured == true)
        {
            builder.Services.AddScoped<OidcUserProvisioner>();

            builder.Services.AddAuthentication()
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
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                    options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        // Keycloak requires client_id or id_token_hint with post_logout_redirect_uri.
                        // Since we don't save tokens, send client_id explicitly.
                        context.ProtocolMessage.ClientId = oidcOptions.ClientId;
                        return Task.CompletedTask;
                    };
                });
        }

        return builder;
    }
}
