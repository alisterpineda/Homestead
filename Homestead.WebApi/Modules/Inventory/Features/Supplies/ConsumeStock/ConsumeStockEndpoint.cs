using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.ConsumeStock;

public static class ConsumeStockEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/consume", async (Guid id,
            ConsumeStockRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new ConsumeStockCommand { SupplyId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("ConsumeStock")
        .Produces<SupplyDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
