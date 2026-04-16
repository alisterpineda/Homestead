using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Identity.Features.Logout;

public static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(), ct);
            return Results.Ok();
        })
        .WithName("Logout")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.AuthenticatedOnly);
    }
}
