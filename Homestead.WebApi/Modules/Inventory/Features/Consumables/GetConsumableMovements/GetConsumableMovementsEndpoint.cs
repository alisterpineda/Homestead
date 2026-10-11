using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumableMovements;

public static class GetConsumableMovementsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/movements", async (Guid id,
            [AsParameters] PaginatedRequest paging, GetConsumableMovementsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, paging, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetConsumableMovements")
        .Produces<PaginatedResponse<StockMovementResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
