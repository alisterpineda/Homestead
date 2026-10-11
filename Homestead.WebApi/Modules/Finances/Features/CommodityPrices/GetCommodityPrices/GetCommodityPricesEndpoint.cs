using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public static class GetCommodityPricesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCommodityPricesQuery request,
            GetCommodityPricesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetCommodityPrices")
        .Produces<PaginatedResponse<CommodityPriceResponse>>();
    }
}
