using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;

public static class GetPriceSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{source}", async (CommodityPriceSource source, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetPriceSourceQuery(source), ct);
            return Results.Ok(response);
        })
        .WithName("GetPriceSource")
        .Produces<GetPriceSourceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
