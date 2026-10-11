using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public class UpsertPriceSourceScheduleHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IPriceSourceScheduleStore scheduleStore,
    IValidator<UpsertPriceSourceScheduleBody> validator)
{
    public async Task<UpsertPriceSourceScheduleResponse> HandleAsync(
        CommodityPriceSource source, UpsertPriceSourceScheduleBody body, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(body, ct);

        source.RequireExternal("scheduled");

        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(body.CronExpression, now);

        // RemoveBySource only marks the existing schedule for deletion; the Add below stages the new one.
        // Both are a single atomic SaveChanges committed by the command pipeline's transaction — no need
        // for an explicit execution strategy or transaction here (which would nest illegally now).
        await scheduleStore.RemoveBySourceAsync(source, ct);

        var schedule = new ProcessingSchedule
        {
            Name = $"{source.DisplayName()} price import",
            Description = "Managed via /finances/settings/price-sources",
            JobType = ProcessingJobType.ImportCommodityPrices,
            CronExpression = body.CronExpression,
            TimeZone = CronSchedule.DefaultTimeZone,
            Priority = body.Priority,
            IsEnabled = body.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            NextRunAt = nextRunAt,
        };
        schedule.SetPayload(new ImportCommodityPricesPayload(source));

        db.ProcessingSchedules.Add(schedule);

        return new UpsertPriceSourceScheduleResponse(schedule.Id, nextRunAt);
    }
}
