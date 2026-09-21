using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

/// <summary>
/// Creates or updates the recurring run for one pairing. Update happens <b>in place</b>: the schedule id
/// is a foreign key on the source row and is surfaced in /admin/schedules and on every job the schedule
/// spawned, so re-saving a cron expression must not mint a new identity and orphan that history.
/// </summary>
public class UpsertBackupScheduleHandler(
    AppDbContext db,
    IBackupSourceCatalog catalog,
    IBackupTargetResolver resolver,
    IBackupTargetGate gate,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpsertBackupScheduleCommand, UpsertBackupScheduleResponse>
{
    /// <summary>
    /// How a schedule reads in the audit log. The cron and its enabled flag live on the
    /// <c>ProcessingSchedule</c> row rather than on the source, so a snapshot diff of the source alone
    /// would come back empty for an in-place edit — and an empty diff is dropped, silently losing the
    /// one change an operator asking "why did backups stop running" is looking for.
    /// </summary>
    internal static string Describe(ProcessingSchedule? schedule) =>
        schedule is null
            ? "none"
            : $"{schedule.CronExpression} ({(schedule.IsEnabled ? "enabled" : "disabled")}), " +
              $"priority {schedule.Priority}";

    public async Task<UpsertBackupScheduleResponse> Handle(
        UpsertBackupScheduleCommand request, CancellationToken ct)
    {
        var source = await db.BackupSources
            .FirstOrDefaultAsync(s => s.Id == request.BackupSourceId, ct)
            ?? throw new EntityNotFoundException("Backup source not found.");

        var descriptor = catalog.Find(source.SourceKind)
            ?? throw new BusinessRuleException(
                $"Backup source kind '{source.SourceKind}' is no longer available in this build.");

        var target = resolver.Resolve(source.TargetKey);
        var verdict = await gate.CheckAsync(target, ct);
        if (!verdict.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{source.TargetKey}' is unavailable: {verdict.Reason}");

        // A DISABLED pairing may still be scheduled — arming the cron before turning the backup on is a
        // reasonable order to work in, and the planner skips a disabled source quietly rather than
        // filling the job list with failures.

        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(request.CronExpression, now);
        var name = $"Backup {descriptor.DisplayName} to {source.TargetKey}";

        // Scheduled runs are never dry and never bypass the delete breaker: an unattended run that could
        // prune the mirror wholesale is precisely what the breaker exists to stop.
        var payload = new BackupRunPayload(source.Id);

        // ScheduleId is SET NULL on delete, but a row deleted in the same transaction we are reading in
        // could still be referenced — so treat "id present but row gone" as "create a new one" rather
        // than trusting the FK to have caught up.
        var schedule = source.ScheduleId is { } scheduleId
            ? await db.ProcessingSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            : null;

        // Captured BEFORE the mutation below, since the tracked entity is edited in place.
        var before = Describe(schedule);

        if (schedule is null)
        {
            schedule = new ProcessingSchedule
            {
                Name = name,
                Description = "Managed via /admin/backup",
                JobType = descriptor.JobType,
                CronExpression = request.CronExpression,
                TimeZone = CronSchedule.DefaultTimeZone,
                Priority = request.Priority,
                IsEnabled = request.IsEnabled,
                CreatedAt = now,
                CreatedByUserId = currentUser.UserId,
                NextRunAt = nextRunAt,
            };
            schedule.SetPayload(payload);

            db.ProcessingSchedules.Add(schedule);
            source.ScheduleId = schedule.Id;
        }
        else
        {
            schedule.Name = name;
            schedule.JobType = descriptor.JobType;
            schedule.CronExpression = request.CronExpression;
            schedule.Priority = request.Priority;
            schedule.IsEnabled = request.IsEnabled;
            schedule.NextRunAt = nextRunAt;
            schedule.SetPayload(payload);
        }

        source.UpdatedAt = now;

        audit.TrackScalar("BackupSource", source.Id, "Schedule", before, Describe(schedule));

        await unitOfWork.SaveChangesAsync(ct);

        return new UpsertBackupScheduleResponse(schedule.Id, nextRunAt);
    }
}
