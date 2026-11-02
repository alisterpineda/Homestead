using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Homestead.WebApi.Authentication;

public static class AuthPolicies
{
    public const string AuthenticatedOnly = "AuthenticatedOnly";
    public const string ActiveUser = "ActiveUser";
    public const string AccountInactiveError = "account_inactive";
    public const string AccountNotLinkedError = "account_not_linked";

    // The MCP door: bearer tokens only (the McpAuth scheme), an active linked user. Registered by AddHomesteadMcp
    // only when the Mcp section is configured.
    public const string McpActiveUser = "McpActiveUser";

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
            // A validated bearer token whose subject is linked to no AppUser (McpBearerPrincipal) gets its own
            // reason, so the result handler can answer 403 account_not_linked instead of a challenge.
            if (context.User.HasClaim(c => c.Type == AppUserPrincipal.UnlinkedClaimType))
                context.Fail(new AuthorizationFailureReason(this, AuthPolicies.AccountNotLinkedError));
            else
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
        if (HasReason(authorizeResult, AuthPolicies.AccountInactiveError))
        {
            await WriteForbiddenAsync(context, "Your account is inactive.", AuthPolicies.AccountInactiveError);
            return;
        }

        // Not a 401: the token is valid, and a challenge would make MCP clients restart OAuth in a loop.
        if (HasReason(authorizeResult, AuthPolicies.AccountNotLinkedError))
        {
            await WriteForbiddenAsync(context,
                "No Homestead account is linked to this identity. Sign in to Homestead in the browser first.",
                AuthPolicies.AccountNotLinkedError);
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool HasReason(PolicyAuthorizationResult result, string reason) =>
        result.Forbidden && result.AuthorizationFailure?.FailureReasons.Any(r => r.Message == reason) == true;

    // WriteAsJsonAsync replaces a preset ContentType with application/json, so the problem type is passed to it.
    private static Task WriteForbiddenAsync(HttpContext context, string detail, string errorCode)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(
            Common.ProblemDetailsFactory.Forbidden(detail, errorCode), options: null,
            contentType: "application/problem+json");
    }
}
