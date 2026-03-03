using System.Security.Claims;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Authentication;

public static class AuthPolicies
{
    public const string AuthenticatedOnly = "AuthenticatedOnly";
    public const string ActiveUser = "ActiveUser";
    public const string AccountInactiveError = "account_inactive";
}

public class ActiveUserRequirement : IAuthorizationRequirement;

public class ActiveUserHandler(
    UserManager<AppUser> userManager)
    : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveUserRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            context.Fail();
            return;
        }

        // Check the IsActive claim first (set at sign-in time) to avoid a DB query
        var isActiveClaim = context.User.FindFirstValue("IsActive");
        if (isActiveClaim is not null)
        {
            if (string.Equals(isActiveClaim, "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return;
            }

            context.Fail(new AuthorizationFailureReason(this, AuthPolicies.AccountInactiveError));
            return;
        }

        // Fallback: query the database (e.g., if claim is missing for legacy sessions)
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            context.Fail();
            return;
        }

        if (!user.IsActive)
        {
            context.Fail(new AuthorizationFailureReason(this, AuthPolicies.AccountInactiveError));
            return;
        }

        context.Succeed(requirement);
    }
}

public class InactiveUserAuthorizationMiddlewareResultHandler(
    IAuthorizationMiddlewareResultHandler defaultHandler)
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons
                .Any(r => r.Message == AuthPolicies.AccountInactiveError) == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = AuthPolicies.AccountInactiveError });
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
