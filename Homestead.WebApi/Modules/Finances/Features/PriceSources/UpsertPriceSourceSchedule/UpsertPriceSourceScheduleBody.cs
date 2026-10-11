namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public record UpsertPriceSourceScheduleBody
{
    public required string CronExpression { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
}
