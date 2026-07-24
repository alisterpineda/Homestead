using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSource;

/// <summary>
/// Removes a backup pairing, cancelling its live run first when the caller opts in.
/// </summary>
/// <remarks>
/// <para>
/// Everything here — the child cancellations, the parent flip, the schedule delete, the row removal —
/// happens inside the one command transaction. That is deliberate: a 409 thrown partway through rolls
/// the cancellation back with it, so a caller who retries re-cancels from a clean slate rather than
/// finding a half-cancelled run. Do not move the cancels outside the transaction to "keep the progress".
/// </para>
/// <para>
/// Cancelling mid-run is safe because batches flush their state rows incrementally and every path is
/// deterministic: whatever landed on the mirror is recorded, and re-creating the same pairing converges
/// on the same layout (at the cost of one full re-put, since the state rows cascade away with the row).
/// </para>
/// </remarks>
public class DeleteBackupSourceHandler(
    AppDbContext db,
    IBackupSourceCatalog catalog,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteBackupSourceCommand, bool>
{
    public async Task<bool> Handle(DeleteBackupSourceCommand request, CancellationToken ct)
    {
        var source = await db.BackupSources.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new EntityNotFoundException("Backup source not found.");

        // EntityId is the pairing's own id, so it identifies the run on its own; the job-type narrowing is
        // belt-and-braces and is dropped when the kind is no longer registered (a stale row must still be
        // deletable). ParentJobId == null is what separates the run from its batch children, which share
        // the parent's EntityId.
        var liveRuns = db.ProcessingJobs.Where(j =>
            j.EntityId == request.Id &&
            j.ParentJobId == null &&
            (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress));

        if (catalog.Find(source.SourceKind) is { } descriptor)
            liveRuns = liveRuns.Where(j => j.Type == descriptor.JobType);

        // Oldest first: single-flight makes two live parents unlikely, not impossible, and cancelling the
        // one that has actually started beats picking whichever row the planner happened to return.
        var liveRun = await liveRuns
            .AsNoTracking()
            .OrderBy(j => j.CreatedAt)
            .Select(j => new { j.Id })
            .FirstOrDefaultAsync(ct);

        if (liveRun is not null)
        {
            if (!request.CancelRun)
                throw new EntityInUseException(
                    "A backup run for this source is in progress. Cancel the run to delete it.");

            var now = DateTimeOffset.UtcNow;

            // Pending children first, and this is race-safe by construction: the processor's claim query
            // picks only Pending rows, so a child that slips past this update was already claimed and is
            // caught by the InProgress check below.
            await db.ProcessingJobs
                .Where(j => j.ParentJobId == liveRun.Id && j.Status == ProcessingJobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Cancelled)
                    .SetProperty(j => j.CompletedAt, now), ct);

            // Re-read: the parent may have moved on (or terminated) while we cancelled its children.
            var parent = await db.ProcessingJobs
                .AsNoTracking()
                .Where(j => j.Id == liveRun.Id)
                .Select(j => new { j.Status, j.TotalChildCount })
                .FirstOrDefaultAsync(ct);

            if (parent is { Status: ProcessingJobStatus.InProgress })
            {
                // TotalChildCount is only written when the planner finishes fanning out, so zero here
                // means the planner itself is executing right now — it is about to insert children
                // referencing a row we are about to delete.
                var plannerRunning = parent.TotalChildCount == 0;

                var childRunning = !plannerRunning && await db.ProcessingJobs.AnyAsync(
                    j => j.ParentJobId == liveRun.Id && j.Status == ProcessingJobStatus.InProgress, ct);

                // Nothing here can stop work already inside a handler, and that handler is mid-way through
                // writing to the target. Refuse rather than delete underneath it; the batch handler's own
                // source-missing backstop covers the narrower race, but a clear "retry shortly" is a far
                // better answer than a half-torn-down mirror.
                if (plannerRunning || childRunning)
                    throw new EntityInUseException(
                        "A backup batch is still finishing — retry shortly.");
            }

            if (parent is not null)
            {
                // Guarded so we only flip a run that is still live. Zero rows affected means it reached a
                // terminal status concurrently, which is exactly the outcome we wanted — proceed.
                await db.ProcessingJobs
                    .Where(j => j.Id == liveRun.Id &&
                                (j.Status == ProcessingJobStatus.Pending ||
                                 j.Status == ProcessingJobStatus.InProgress))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, ProcessingJobStatus.Cancelled)
                        .SetProperty(j => j.CompletedAt, now), ct);
            }
        }

        // The schedule goes with the pairing it drove. Job rows that ran from it keep their history —
        // their ScheduleId is SET NULL by the FK, not cascaded away.
        if (source.ScheduleId is { } scheduleId)
        {
            var schedule = await db.ProcessingSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
            if (schedule is not null)
                db.ProcessingSchedules.Remove(schedule);
        }

        audit.Deleted("BackupSource", source.Id, $"{source.SourceKind} → {source.TargetKey}");

        // The state rows go with it via the cascade — deleting a pairing is an explicit "forget this
        // backup". The mirror's files are left alone; the target is one-way.
        db.BackupSources.Remove(source);

        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
