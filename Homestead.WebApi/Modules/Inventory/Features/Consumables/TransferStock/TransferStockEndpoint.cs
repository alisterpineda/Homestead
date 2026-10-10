using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TransferStock;

public static class TransferStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/transfer", async (Guid id,
            TransferStockRequest body, TransferStockHandler handler, CancellationToken ct) =>
        {
            var command = new TransferStockCommand { ConsumableId = id, Body = body };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("TransferStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
