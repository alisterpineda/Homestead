using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Homestead.WebApi.Authentication;

public static class AuthPolicies
{
    public const string AuthenticatedOnly = "AuthenticatedOnly";
    public const string ActiveUser = "ActiveUser";
    public const string AccountInactiveError = "account_inactive";

    public const string RequireAdmin = "RequireAdmin";
    public const string RequireContributor = "RequireContributor";
    public const string RequireViewer = "RequireViewer";
}

public class ActiveUserRequirement : IAuthorizationRequirement;

public class ActiveUserHandler : AuthorizationHandler<ActiveUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveUserRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var isActiveClaim = context.User.FindFirstValue(AppUserPrincipal.IsActiveClaimType);
        if (string.Equals(isActiveClaim, "true", StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);
        else
            context.Fail(new AuthorizationFailureReason(this, AuthPolicies.AccountInactiveError));

        return Task.CompletedTask;
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
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                Common.ProblemDetailsFactory.Forbidden("Your account is inactive.", AuthPolicies.AccountInactiveError));
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
