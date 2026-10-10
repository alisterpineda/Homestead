using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;

public static class GetPriceSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{source}", async (CommodityPriceSource source, GetPriceSourceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetPriceSourceQuery(source), ct);
            return Results.Ok(response);
        })
        .WithName("GetPriceSource")
        .Produces<GetPriceSourceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
