using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public static class LoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (LoginCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);

            if (response.IsInactive)
                return Results.Json(
                    ProblemDetailsFactory.Unauthorized(
                        "Your account is inactive. Please contact an administrator.",
                        AuthPolicies.AccountInactiveError),
                    statusCode: StatusCodes.Status401Unauthorized,
                    contentType: "application/problem+json");

            if (response.IsLockedOut)
                return Results.Json(
                    ProblemDetailsFactory.Unauthorized(
                        "Too many failed login attempts. Your account has been temporarily locked.",
                        AuthPolicies.AccountLockedError),
                    statusCode: StatusCodes.Status401Unauthorized,
                    contentType: "application/problem+json");

            return response.Succeeded
                ? Results.Ok()
                : Results.Unauthorized();
        })
        .WithName("Login")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .AllowAnonymous();
    }
}
