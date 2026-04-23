using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;

public static class GetCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetCommodityQuery(id), ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetCommodity")
        .Produces<CommodityResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
