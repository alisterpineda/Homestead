using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.ReceiveStock;

public static class ReceiveStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/receive", async (Guid id,
            ReceiveStockRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new ReceiveStockCommand { SupplyId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("ReceiveStock")
        .Produces<SupplyDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
