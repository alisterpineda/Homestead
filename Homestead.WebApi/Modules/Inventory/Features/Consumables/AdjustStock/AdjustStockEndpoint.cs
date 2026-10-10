using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;

public static class AdjustStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/adjust", async (Guid id,
            AdjustStockRequest body, AdjustStockHandler handler, CancellationToken ct) =>
        {
            var command = new AdjustStockCommand { ConsumableId = id, Body = body };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("AdjustStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
