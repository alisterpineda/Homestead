using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.AdjustStock;

public static class AdjustStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/adjust", async (Guid id,
            AdjustStockRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new AdjustStockCommand { SupplyId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("AdjustStock")
        .Produces<SupplyDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
