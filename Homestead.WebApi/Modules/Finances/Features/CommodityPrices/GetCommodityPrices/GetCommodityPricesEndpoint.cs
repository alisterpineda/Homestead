using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public static class GetCommodityPricesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCommodityPricesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetCommodityPrices")
        .Produces<PaginatedResponse<CommodityPriceResponse>>();
    }
}
