using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public static class UpsertPriceSourceScheduleEndpoint
{
    public record UpsertPriceSourceScheduleBody
    {
        public required string CronExpression { get; init; }
        public bool IsEnabled { get; init; } = true;
        public int Priority { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{source}/schedule",
            async (CommodityPriceSource source, UpsertPriceSourceScheduleBody body,
                UpsertPriceSourceScheduleHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new UpsertPriceSourceScheduleCommand
            {
                Source = source,
                CronExpression = body.CronExpression,
                IsEnabled = body.IsEnabled,
                Priority = body.Priority,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("UpsertPriceSourceSchedule")
        .Produces<UpsertPriceSourceScheduleResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
