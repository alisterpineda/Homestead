namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

public static class GetPlaceContentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/contents", async ([AsParameters] GetPlaceContentsQuery request,
            GetPlaceContentsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetPlaceContents")
        .Produces<PlaceContentsResponse>();
    }
}
