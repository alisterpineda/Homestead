using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;

public static class GetCommoditiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCommoditiesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetCommodities")
        .Produces<PaginatedResponse<CommodityResponse>>();
    }
}
