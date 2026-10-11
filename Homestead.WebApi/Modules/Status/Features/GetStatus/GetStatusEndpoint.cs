namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public static class GetStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetStatusHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetStatus")
        .Produces<GetStatusResponse>()
        .AllowAnonymous();
    }
}
