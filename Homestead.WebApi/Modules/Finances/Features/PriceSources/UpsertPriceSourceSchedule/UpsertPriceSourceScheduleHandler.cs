using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public class UpsertPriceSourceScheduleHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IPriceSourceScheduleStore scheduleStore)
    : IRequestHandler<UpsertPriceSourceScheduleCommand, UpsertPriceSourceScheduleResponse>
{
    public async Task<UpsertPriceSourceScheduleResponse> Handle(
        UpsertPriceSourceScheduleCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("scheduled");

        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(request.CronExpression, now);
        var newId = Guid.NewGuid();

        // RemoveBySource only marks the existing schedule for deletion; the Add below stages the new one.
        // Both are a single atomic SaveChanges committed by the command pipeline's transaction — no need
        // for an explicit execution strategy or transaction here (which would nest illegally now).
        await scheduleStore.RemoveBySourceAsync(request.Source, ct);

        var schedule = new ProcessingSchedule
        {
            Id = newId,
            Name = $"{request.Source.DisplayName()} price import",
            Description = "Managed via /finances/settings/price-sources",
            JobType = ProcessingJobType.ImportCommodityPrices,
            CronExpression = request.CronExpression,
            TimeZone = CronSchedule.DefaultTimeZone,
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            NextRunAt = nextRunAt,
        };
        schedule.SetPayload(new ImportCommodityPricesPayload(request.Source));

        db.ProcessingSchedules.Add(schedule);

        return new UpsertPriceSourceScheduleResponse(newId, nextRunAt);
    }
}
