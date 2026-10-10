using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSchedule;

/// <summary>Drops one pairing's recurring run, leaving it manual-only.</summary>
public class DeleteBackupScheduleHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeleteBackupScheduleCommand request, CancellationToken ct)
    {
        var source = await db.BackupSources
            .FirstOrDefaultAsync(s => s.Id == request.BackupSourceId, ct)
            ?? throw new EntityNotFoundException("Backup source not found.");

        if (source.ScheduleId is not { } scheduleId)
            throw new EntityNotFoundException("Backup schedule not found.");

        var schedule = await db.ProcessingSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is not null)
            db.ProcessingSchedules.Remove(schedule);

        // Disarming an unattended backup is exactly the change "why did backups stop running on the 3rd"
        // needs to find. Described rather than snapshot-diffed for the same reason as the upsert: the
        // cron lives on the schedule row, not on the source.
        audit.TrackScalar(
            "BackupSource", source.Id, "Schedule",
            UpsertBackupScheduleHandler.Describe(schedule),
            UpsertBackupScheduleHandler.Describe(null));

        // Explicitly nulled rather than left to the FK's SET NULL: the source row is tracked in this same
        // context, so leaving it stale would let the response and any follow-up read in this request
        // still see a schedule that no longer exists.
        source.ScheduleId = null;
        source.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
