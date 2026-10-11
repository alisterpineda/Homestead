using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;

public static class AdjustStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/adjust", async (Guid id,
            AdjustStockRequest body, AdjustStockHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("AdjustStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
