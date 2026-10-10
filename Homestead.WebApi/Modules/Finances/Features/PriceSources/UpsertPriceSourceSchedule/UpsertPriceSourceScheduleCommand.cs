using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public record UpsertPriceSourceScheduleCommand
{
    public CommodityPriceSource Source { get; init; }
    public required string CronExpression { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
}

public record UpsertPriceSourceScheduleResponse(Guid Id, DateTimeOffset NextRunAt);
