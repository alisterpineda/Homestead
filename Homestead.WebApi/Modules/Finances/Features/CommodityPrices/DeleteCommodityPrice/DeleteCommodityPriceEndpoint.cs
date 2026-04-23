using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrice;

public static class DeleteCommodityPriceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var deleted = await sender.Send(new DeleteCommodityPriceCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCommodityPrice")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
