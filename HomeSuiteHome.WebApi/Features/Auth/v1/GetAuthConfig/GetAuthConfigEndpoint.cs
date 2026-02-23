namespace HomeSuiteHome.WebApi.Features.Auth.v1.GetAuthConfig;

public static class GetAuthConfigEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/config", async (GetAuthConfigHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(new GetAuthConfigRequest(), ct);
            return Results.Ok(response);
        })
        .AllowAnonymous()
        .WithName("GetAuthConfig")
        .Produces<GetAuthConfigResponse>();
    }
}
