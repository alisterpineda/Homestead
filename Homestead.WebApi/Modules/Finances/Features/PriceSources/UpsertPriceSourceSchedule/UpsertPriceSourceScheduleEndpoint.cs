using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public static class UpsertPriceSourceScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{source}/schedule",
            async (CommodityPriceSource source, UpsertPriceSourceScheduleBody body,
                UpsertPriceSourceScheduleHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(source, body, ct)))
        .WithName("UpsertPriceSourceSchedule")
        .Produces<UpsertPriceSourceScheduleResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
