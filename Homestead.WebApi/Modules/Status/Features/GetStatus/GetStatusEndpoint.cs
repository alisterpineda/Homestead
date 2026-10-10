namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public static class GetStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetStatusHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetStatusQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetStatus")
        .Produces<GetStatusResponse>()
        .AllowAnonymous();
    }
}
