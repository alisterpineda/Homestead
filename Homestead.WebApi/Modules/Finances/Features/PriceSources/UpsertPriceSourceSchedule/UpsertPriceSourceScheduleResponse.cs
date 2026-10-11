namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public record UpsertPriceSourceScheduleResponse(Guid Id, DateTimeOffset NextRunAt);
