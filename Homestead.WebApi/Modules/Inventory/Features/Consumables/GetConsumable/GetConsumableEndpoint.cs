namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public static class GetConsumableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetConsumableHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetConsumable")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
