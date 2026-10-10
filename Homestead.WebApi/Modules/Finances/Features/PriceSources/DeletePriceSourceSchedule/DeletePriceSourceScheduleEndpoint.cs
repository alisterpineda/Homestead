using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;

public static class DeletePriceSourceScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{source}/schedule",
            async (CommodityPriceSource source, DeletePriceSourceScheduleHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new DeletePriceSourceScheduleCommand(source), ct);
            return Results.NoContent();
        })
        .WithName("DeletePriceSourceSchedule")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
