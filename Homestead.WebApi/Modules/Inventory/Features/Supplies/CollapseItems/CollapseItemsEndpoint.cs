using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.CollapseItems;

public static class CollapseItemsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/collapse", async (Guid id,
            CollapseItemsRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CollapseItemsCommand { SupplyId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("CollapseItems")
        .Produces<SupplyDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
