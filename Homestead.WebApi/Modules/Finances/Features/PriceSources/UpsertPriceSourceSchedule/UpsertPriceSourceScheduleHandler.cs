using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
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
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new UpsertPriceSourceScheduleResponse(newId, nextRunAt);
    }
}
