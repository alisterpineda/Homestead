namespace Homestead.WebApi.Features.Identity.v1.Logout;

public static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", async (LogoutHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new LogoutRequest(), ct);
            return Results.Ok();
        })
        .WithName("Logout")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization();
    }
}
