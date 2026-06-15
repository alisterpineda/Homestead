using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TransferStock;

public static class TransferStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/transfer", async (Guid id,
            TransferStockRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new TransferStockCommand { ConsumableId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("TransferStock")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
