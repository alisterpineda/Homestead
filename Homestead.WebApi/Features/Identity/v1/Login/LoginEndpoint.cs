namespace Homestead.WebApi.Features.Identity.v1.Login;

public static class LoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (LoginRequest request, LoginHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
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
