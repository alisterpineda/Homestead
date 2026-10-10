using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ReceiveStock;

public static class ReceiveStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/receive", async (Guid id,
            ReceiveStockRequest body, ReceiveStockHandler handler, CancellationToken ct) =>
        {
            var command = new ReceiveStockCommand { ConsumableId = id, Body = body };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("ReceiveStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
