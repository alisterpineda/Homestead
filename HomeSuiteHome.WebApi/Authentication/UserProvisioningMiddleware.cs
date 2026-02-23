using System.Security.Claims;
using HomeSuiteHome.WebApi.Domains.Identity;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeSuiteHome.WebApi.Authentication;

public class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IOptions<AuthOptions> authOptions)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claimMappings = authOptions.Value.ClaimMappings;
            var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue(claimMappings.Subject);

            if (sub is not null)
            {
                var appUser = await db.AppUsers
                    .FirstOrDefaultAsync(u => u.ExternalSubject == sub, context.RequestAborted);

                if (appUser is null)
                {
                    var email = context.User.FindFirstValue(ClaimTypes.Email)
                                ?? context.User.FindFirstValue(claimMappings.Email)
                                ?? string.Empty;

                    var name = context.User.FindFirstValue(ClaimTypes.Name)
                               ?? context.User.FindFirstValue(claimMappings.Name)
                               ?? sub;

                    appUser = new AppUser
                    {
                        Id = Guid.NewGuid(),
                        ExternalSubject = sub,
                        DisplayName = name,
                        Email = email,
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    db.AppUsers.Add(appUser);
                    await db.SaveChangesAsync(context.RequestAborted);
                }

                currentUserAccessor.User = appUser;
            }
        }

        await next(context);
    }
}
