using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HomeSuiteHome.WebApi.Authentication;

public static class AuthenticationExtensions
{
    public static WebApplicationBuilder AddAppAuthentication(this WebApplicationBuilder builder)
    {
        var authOptions = builder.Configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        builder.Services.Configure<AuthOptions>(
            builder.Configuration.GetSection(AuthOptions.SectionName));

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authOptions.Authority;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAudience = authOptions.Audience,
                    ValidateAudience = !string.IsNullOrEmpty(authOptions.Audience)
                };

                var claimMappings = authOptions.ClaimMappings;

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // SignalR sends the access token via query string for WebSocket connections
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity is null) return Task.CompletedTask;

                        // Normalize subject claim
                        var subClaim = identity.FindFirst(claimMappings.Subject);
                        if (subClaim is not null && !identity.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subClaim.Value));
                        }

                        // Normalize email claim
                        var emailClaim = identity.FindFirst(claimMappings.Email);
                        if (emailClaim is not null && !identity.HasClaim(c => c.Type == ClaimTypes.Email))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Email, emailClaim.Value));
                        }

                        // Normalize name claim
                        var nameClaim = identity.FindFirst(claimMappings.Name);
                        if (nameClaim is not null && !identity.HasClaim(c => c.Type == ClaimTypes.Name))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Name, nameClaim.Value));
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddFallbackPolicy("authenticated", policy =>
                policy.RequireAuthenticatedUser());

        builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

        return builder;
    }
}
