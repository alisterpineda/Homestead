using System.Security.Claims;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Unit.Authentication;

// ActiveUserHandler tells three failures apart: an inactive user (403 account_inactive), a token whose subject is
// linked to no Homestead account (403 account_not_linked — a 401 would send MCP clients round the OAuth loop), and a
// principal with no user id at all (a plain failure the default handler turns into 401/403).
public class ActiveUserHandlerTests
{
    private static async Task<AuthorizationHandlerContext> Authorize(ClaimsPrincipal user)
    {
        var requirement = new ActiveUserRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new ActiveUserHandler().HandleAsync(context);
        return context;
    }

    private static AppUser User(bool isActive) => new()
    {
        Id = Guid.Parse("0198a7c2-0000-7000-8000-000000000001"),
        Email = "user@test.com",
        DisplayName = "User",
        Role = AppRole.Viewer,
        IsActive = isActive,
    };

    [Fact]
    public async Task UnlinkedPrincipal_FailsWithAccountNotLinked()
    {
        var unlinked = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppUserPrincipal.UnlinkedClaimType, "true")], "Bearer"));

        var context = await Authorize(unlinked);

        Assert.True(context.HasFailed);
        var reason = Assert.Single(context.FailureReasons);
        Assert.Equal("account_not_linked", reason.Message);
    }

    [Fact]
    public async Task MissingUserId_WithoutUnlinkedClaim_FailsWithNoReason()
    {
        var anonymousShaped = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@test.com")], "Bearer"));

        var context = await Authorize(anonymousShaped);

        Assert.True(context.HasFailed);
        Assert.Empty(context.FailureReasons);
    }

    [Fact]
    public async Task ActiveUser_Succeeds()
    {
        var context = await Authorize(AppUserPrincipal.Build(User(isActive: true)));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task InactiveUser_FailsWithAccountInactive()
    {
        var context = await Authorize(AppUserPrincipal.Build(User(isActive: false)));

        var reason = Assert.Single(context.FailureReasons);
        Assert.Equal("account_inactive", reason.Message);
    }

    [Fact]
    public async Task ResultHandler_MapsAccountNotLinked_ToA403Problem()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        var fallback = new RecordingResultHandler();
        var sut = new InactiveUserAuthorizationMiddlewareResultHandler(fallback);
        var failure = AuthorizationFailure.Failed(
            [new AuthorizationFailureReason(new ActiveUserHandler(), "account_not_linked")]);

        await sut.HandleAsync(_ => Task.CompletedTask, http, new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build(),
            PolicyAuthorizationResult.Forbid(failure));

        Assert.False(fallback.Called);
        Assert.Equal(403, http.Response.StatusCode);
        Assert.Equal("application/problem+json", http.Response.ContentType);
        http.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("No Homestead account is linked to this identity. Sign in to Homestead in the browser first.",
            body.RootElement.GetProperty("detail").GetString());
        Assert.Equal("account_not_linked", body.RootElement.GetProperty("errorCode").GetString());
    }

    private sealed class RecordingResultHandler : IAuthorizationMiddlewareResultHandler
    {
        public bool Called { get; private set; }

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
