using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumableMovements;

public static class GetConsumableMovementsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/movements", async (Guid id,
            [AsParameters] PaginatedRequest paging, GetConsumableMovementsHandler handler, CancellationToken ct) =>
        {
            var query = new GetConsumableMovementsQuery
            {
                Id = id,
                Page = paging.Page,
                PageSize = paging.PageSize,
            };
            var response = await handler.Handle(query, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetConsumableMovements")
        .Produces<PaginatedResponse<StockMovementResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
