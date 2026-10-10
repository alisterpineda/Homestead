namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public static class GetTagsByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetInventoryTagsByIdsQuery request,
            GetTagsByIdsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetInventoryTagsByIds")
        .Produces<IReadOnlyList<GetInventoryTagsByIdsResponse>>();
    }
}
