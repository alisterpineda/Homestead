using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Infrastructure.Backup;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Injects only what the derivation needs: the job tables, and the catalog — which supplies the set of
/// planner job types, because a pairing's runs are identified by the job type its own kind registered.
/// </para>
/// <para>
/// Known cost, recorded rather than fixed: <c>ProcessingJobConfiguration</c> indexes <c>EntityId</c>
/// alone, and batch children carry their parent's <c>EntityId</c>, so these lookups walk every batch
/// child ever created for a pairing and nothing prunes <c>processing.jobs</c>. Acceptable for admin-only,
/// low-frequency surfaces. If it bites, add a <c>(EntityId, ParentJobId, CreatedAt DESC)</c> index.
/// </para>
/// </remarks>
public class BackupRunHistory(AppDbContext db, IBackupSourceCatalog catalog) : IBackupRunHistory
{
    /// <summary>
    /// How many failed-child messages one run reports. A large run can fail many batches, and a reader
    /// needs the shape of the problem, not every line of it — /admin/jobs remains the exhaustive record.
    /// </summary>
    private const int MaxChildErrors = 5;

    /// <summary>
    /// A run that mirrored nothing at all. Matched as jsonb containment rather than by deserializing every
    /// row, so the exclusion happens in the database and the query stays one round trip.
    /// </summary>
    /// <remarks>
    /// PascalCase because <c>ProcessingJob.SetResult</c> serializes with no naming policy, and both flags
    /// are plain <c>bool</c>s on <see cref="BackupRunSummary"/> so they are always present to match.
    /// </remarks>
    private const string SkippedRunMarker = """{"Skipped":true}""";
    private const string DryRunMarker = """{"DryRun":true}""";

    public async Task<Dictionary<Guid, BackupRunStatus>> GetLastRunsAsync(
        IReadOnlyList<Guid> sourceIds, CancellationToken ct)
    {
        var jobTypes = catalog.All().Select(d => d.JobType).Distinct().ToList();

        if (sourceIds.Count == 0 || jobTypes.Count == 0)
            return [];

        // Newest parent run per pairing. EntityId is indexed; ParentJobId == null is what excludes the
        // batch children, which carry their parent's EntityId. The grouped ordering keeps this one query
        // rather than one per pairing.
        var rows = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => jobTypes.Contains(j.Type)
                        && j.EntityId != null
                        && sourceIds.Contains(j.EntityId.Value)
                        && j.ParentJobId == null)
            .GroupBy(j => j.EntityId!.Value)
            .Select(g => g
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new
                {
                    EntityId = j.EntityId!.Value,
                    j.Id,
                    j.Status,
                    j.CreatedAt,
                    j.StartedAt,
                    j.CompletedAt,
                    j.Error,
                    j.TotalChildCount,
                    j.CompletedChildCount,
                    j.FailedChildCount,
                    j.Result,
                })
                .First())
            .ToListAsync(ct);

        // A parent whose CHILDREN failed carries no Error of its own: the generic
        // JobProcessorService.UpdateParentProgressAsync sets the parent's status from the child tally but
        // never writes an error string, and only a throw from the parent's own handler populates one. So
        // without this the admin page shows a red "Failed" badge with no explanation anywhere, and the
        // only record of WHICH files failed sits on the child rows in /admin/jobs.
        var failedParentIds = rows
            .Where(r => r.FailedChildCount > 0)
            .Select(r => r.Id)
            .ToList();

        // Result comes back alongside Error: a failed batch stages a typed outcome there, and the card
        // needs the code rather than the message — an out-of-space run must render as one sentence, not
        // as a stack of identical fragments, and message strings are never parsed to decide that.
        var childErrors = failedParentIds.Count == 0
            ? []
            : await db.ProcessingJobs
                .AsNoTracking()
                .Where(j => j.ParentJobId != null
                            && failedParentIds.Contains(j.ParentJobId.Value)
                            && j.Status == ProcessingJobStatus.Failed)
                .Select(j => new { ParentJobId = j.ParentJobId!.Value, j.Error, j.Result })
                .ToListAsync(ct);

        var errorsByParent = childErrors
            .Where(e => e.Error != null)
            .GroupBy(e => e.ParentJobId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(e => e.Error!).Take(MaxChildErrors).ToList());

        var outOfSpaceParents = childErrors
            .Where(e => DeserializeOutcome(e.Result)?.ErrorCode == BackupBatchOutcome.OutOfSpaceCode)
            .Select(e => e.ParentJobId)
            .ToHashSet();

        return rows.ToDictionary(r => r.EntityId, r => new BackupRunStatus
        {
            JobId = r.Id,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            StartedAt = r.StartedAt,
            CompletedAt = r.CompletedAt,
            Error = r.Error,
            TotalChildCount = r.TotalChildCount,
            CompletedChildCount = r.CompletedChildCount,
            FailedChildCount = r.FailedChildCount,
            OutOfSpace = outOfSpaceParents.Contains(r.Id),
            BatchErrors = errorsByParent.GetValueOrDefault(r.Id, []),
            Summary = DeserializeSummary(r.Result),
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A separate query from the newest-run one on purpose: the two answer different questions, and the
    /// gap between them is the signal. "Last run: failed, 4 minutes ago" and "last successful run: 38
    /// days ago" is a backup that has been quietly broken for over a month, which no single row says.
    /// </para>
    /// <para>
    /// <c>Completed</c> alone is not enough, which is why the markers are excluded here. A skipped run —
    /// the pairing disabled, the target switched off — stages <c>Skipped = true</c>, zeroes its child
    /// count and completes <i>normally</i>, so a paused backup would otherwise report a fresh success on
    /// every cron tick, forever. A dry run is the same shape: it plans and stages, and copies nothing.
    /// Since this line is the catch-all for every way a backup dies silently, it is the last value in the
    /// system that may be easy to fake.
    /// </para>
    /// <para>
    /// A null <c>Result</c> still counts: an older row that completed before any summary was staged is a
    /// genuine run, and treating it as a skip would invent staleness that never happened.
    /// </para>
    /// </remarks>
    public async Task<Dictionary<Guid, DateTimeOffset>> GetLastSuccessfulRunsAsync(
        IReadOnlyList<Guid> sourceIds, CancellationToken ct)
    {
        var jobTypes = catalog.All().Select(d => d.JobType).Distinct().ToList();

        if (sourceIds.Count == 0 || jobTypes.Count == 0)
            return [];

        var rows = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => jobTypes.Contains(j.Type)
                        && j.EntityId != null
                        && sourceIds.Contains(j.EntityId.Value)
                        && j.ParentJobId == null
                        && j.Status == ProcessingJobStatus.Completed
                        && j.CompletedAt != null
                        && (j.Result == null
                            || (!EF.Functions.JsonContains(j.Result, SkippedRunMarker)
                                && !EF.Functions.JsonContains(j.Result, DryRunMarker))))
            .GroupBy(j => j.EntityId!.Value)
            .Select(g => new { EntityId = g.Key, CompletedAt = g.Max(j => j.CompletedAt) })
            .ToListAsync(ct);

        return rows
            .Where(r => r.CompletedAt is not null)
            .ToDictionary(r => r.EntityId, r => r.CompletedAt!.Value);
    }

    // Joined by the source row's own ScheduleId. The old shape had to load every BackupToTarget schedule
    // and match a target key out of the jsonb payload in memory; the FK makes that a plain id lookup.
    public async Task<Dictionary<Guid, BackupScheduleSummary>> GetSchedulesAsync(
        IReadOnlyList<Guid> scheduleIds, CancellationToken ct)
    {
        var distinct = scheduleIds.Distinct().ToList();

        if (distinct.Count == 0)
            return [];

        var rows = await db.ProcessingSchedules
            .AsNoTracking()
            .Where(s => distinct.Contains(s.Id))
            .Select(s => new BackupScheduleSummary
            {
                Id = s.Id,
                CronExpression = s.CronExpression,
                IsEnabled = s.IsEnabled,
                Priority = s.Priority,
                NextRunAt = s.NextRunAt,
                LastRunAt = s.LastRunAt,
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id);
    }

    public async Task<IReadOnlyList<BackupActionFailure>> GetActionFailuresAsync(
        Guid parentJobId, CancellationToken ct)
    {
        // Drives the (ParentJobId, Status) index. Only the staged result is read — the joined Error string
        // is a rolled-up, truncated sentence and cannot be attributed to one file.
        //
        // Ordered oldest-first, and that is load-bearing rather than cosmetic: a run can retry the same
        // version in a continuation batch, so a caller that keeps the LAST failure per version is only
        // keeping the newest attempt if the sequence it reads is chronological. Without an ORDER BY the
        // row order is whatever the plan happens to produce, and which message a file gets is arbitrary.
        var staged = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.ParentJobId == parentJobId && j.Status == ProcessingJobStatus.Failed)
            .OrderBy(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Select(j => j.Result)
            .ToListAsync(ct);

        return
        [
            .. staged
                .Select(DeserializeOutcome)
                .Where(o => o?.Failures is { Count: > 0 })
                .SelectMany(o => o!.Failures!)
        ];
    }

    // The staged result is handler-written jsonb. A shape change across a deploy would leave older rows
    // undeserializable; degrade to "no summary" rather than failing the whole page.
    private static BackupRunSummary? DeserializeSummary(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BackupRunSummary>(result);
        }
        // NotSupportedException too, not only JsonException: a row whose shape has drifted far enough
        // that a member no longer maps at all throws that instead, and catching the narrower type would
        // take the whole page down for exactly the case this posture exists to survive.
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    // Same degrade-don't-fail posture as the summary above: an older row whose shape has since changed
    // simply reports no outcome rather than taking the whole page down.
    private static BackupBatchOutcome? DeserializeOutcome(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BackupBatchOutcome>(result);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
