using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Registers a test-only middleware that signs in a user by email, bypassing the IsActive check.
/// This allows integration tests to obtain an authenticated session for inactive users.
/// </summary>
public class TestForceLoginStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == "/test/force-login")
                {
                    var email = context.Request.Query["email"].ToString();
                    if (string.IsNullOrEmpty(email))
                    {
                        context.Response.StatusCode = 400;
                        return;
                    }

                    var userManager = context.RequestServices.GetRequiredService<UserManager<AppUser>>();
                    var signInManager = context.RequestServices.GetRequiredService<SignInManager<AppUser>>();

                    var user = await userManager.FindByEmailAsync(email);
                    if (user is null)
                    {
                        context.Response.StatusCode = 404;
                        return;
                    }

                    await signInManager.SignInAsync(user, isPersistent: true);
                    context.Response.StatusCode = 200;
                    return;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}
