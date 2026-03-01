namespace Homestead.WebApi.Features.Status.GetStatus;

public static class GetStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetStatusHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(new GetStatusRequest(), ct);
            return Results.Ok(response);
        })
        .WithName("GetStatus")
        .Produces<GetStatusResponse>()
        .AllowAnonymous();
    }
}
