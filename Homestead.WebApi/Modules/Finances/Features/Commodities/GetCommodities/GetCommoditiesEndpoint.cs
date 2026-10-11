using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;

public static class GetCommoditiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetCommoditiesQuery request,
            GetCommoditiesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetCommodities")
        .Produces<PaginatedResponse<CommodityResponse>>();
    }
}
