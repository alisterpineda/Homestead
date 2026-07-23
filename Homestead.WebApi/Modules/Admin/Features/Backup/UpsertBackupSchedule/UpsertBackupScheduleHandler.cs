using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Admin.Services;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public class UpsertBackupScheduleHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    ICurrentUserAccessor currentUser,
    IBackupScheduleStore scheduleStore)
    : IRequestHandler<UpsertBackupScheduleCommand, UpsertBackupScheduleResponse>
{
    public async Task<UpsertBackupScheduleResponse> Handle(
        UpsertBackupScheduleCommand request, CancellationToken ct)
    {
        var target = resolver.All()
            .FirstOrDefault(t => string.Equals(t.Key, request.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(request.CronExpression, now);
        var newId = Guid.NewGuid();

        // RemoveByTarget only marks the existing schedule for deletion; the Add below stages the new one.
        // Both are a single atomic SaveChanges committed by the command pipeline's transaction — no need
        // for an explicit execution strategy or transaction here (which would nest illegally now).
        await scheduleStore.RemoveByTargetAsync(target.Key, ct);

        var schedule = new ProcessingSchedule
        {
            Id = newId,
            Name = $"Backup to {target.Key}",
            Description = "Managed via /admin/backup",
            JobType = ProcessingJobType.BackupToTarget,
            CronExpression = request.CronExpression,
            TimeZone = CronSchedule.DefaultTimeZone,
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            NextRunAt = nextRunAt,
        };

        // Scheduled runs are never dry and never bypass the delete breaker: an unattended run that could
        // prune the mirror wholesale is precisely what the breaker exists to stop.
        schedule.SetPayload(new BackupToTargetPayload(target.Key));

        db.ProcessingSchedules.Add(schedule);

        return new UpsertBackupScheduleResponse(newId, nextRunAt);
    }
}
