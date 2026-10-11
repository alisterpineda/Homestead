using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ConsumeStock;

public static class ConsumeStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/consume", async (Guid id,
            ConsumeStockRequest body, ConsumeStockHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("ConsumeStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
