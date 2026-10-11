namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public static class GetItemEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetItemHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItem")
        .Produces<ItemDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
