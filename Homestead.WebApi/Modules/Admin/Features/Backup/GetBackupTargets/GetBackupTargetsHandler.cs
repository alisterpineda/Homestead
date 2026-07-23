using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Admin.Services;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

/// <summary>
/// Assembles the admin backup page: config (from DI), operator state and copy counts (from the backup
/// schema), last-run status (derived from <c>processing.jobs</c>), and the schedule (from the shared
/// schedule table).
/// </summary>
public class GetBackupTargetsHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IBackupScheduleStore scheduleStore)
    : IRequestHandler<GetBackupTargetsQuery, GetBackupTargetsResponse>
{
    public async Task<GetBackupTargetsResponse> Handle(
        GetBackupTargetsQuery request, CancellationToken ct)
    {
        // Config is the single source of truth for which targets exist — a state row for a key that was
        // removed from config is stale bookkeeping, not a target, and must not appear as one.
        var targets = resolver.All();

        var stateByKey = await db.BackupTargetStates
            .AsNoTracking()
            .ToDictionaryAsync(s => s.TargetKey, StringComparer.OrdinalIgnoreCase, ct);

        var totals = await db.DocumentBackupStates
            .AsNoTracking()
            .GroupBy(s => s.TargetKey)
            .Select(g => new { TargetKey = g.Key, Count = g.Count(), Bytes = g.Sum(s => s.FileSize) })
            .ToListAsync(ct);

        var totalsByKey = totals.ToDictionary(t => t.TargetKey, StringComparer.OrdinalIgnoreCase);

        var schedulesByKey = await scheduleStore.GetAllAsync(ct);

        var entityIds = targets.ToDictionary(t => t.Key, t => DeterministicGuid.FromKey(t.Key));
        var lastRuns = await LoadLastRunsAsync(entityIds, ct);

        var items = new List<BackupTargetResponse>(targets.Count);

        foreach (var target in targets)
        {
            stateByKey.TryGetValue(target.Key, out var state);
            totalsByKey.TryGetValue(target.Key, out var total);
            schedulesByKey.TryGetValue(target.Key, out var schedule);
            lastRuns.TryGetValue(entityIds[target.Key], out var lastRun);

            items.Add(new BackupTargetResponse
            {
                Key = target.Key,
                Kind = target.Kind,
                Location = target.Location,
                IsAvailable = target.IsAvailable,
                ConfigurationError = target.ConfigurationError,
                // No row means "never configured by an operator", which reads as enabled: defining a
                // target in config is the opt-in, and requiring a second in-app toggle would make a
                // freshly-deployed schedule silently do nothing.
                IsEnabled = state?.IsEnabled ?? true,
                PruneBlockedAt = state?.PruneBlockedAt,
                PruneBlockedCount = state?.PruneBlockedCount,
                BackedUpFileCount = total?.Count ?? 0,
                BackedUpBytes = total?.Bytes ?? 0,
                LastRun = lastRun,
                Schedule = schedule is null ? null : new BackupScheduleSummary
                {
                    Id = schedule.Id,
                    CronExpression = schedule.CronExpression,
                    IsEnabled = schedule.IsEnabled,
                    Priority = schedule.Priority,
                    NextRunAt = schedule.NextRunAt,
                    LastRunAt = schedule.LastRunAt,
                },
            });
        }

        return new GetBackupTargetsResponse { Items = items };
    }

    private async Task<Dictionary<Guid, BackupRunStatus>> LoadLastRunsAsync(
        Dictionary<string, Guid> entityIds, CancellationToken ct)
    {
        if (entityIds.Count == 0)
            return [];

        var ids = entityIds.Values.ToList();

        // Newest parent run per target. Indexed on EntityId; the grouped ordering keeps this one query
        // rather than one per target.
        var rows = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.Type == ProcessingJobType.BackupToTarget
                        && j.EntityId != null
                        && ids.Contains(j.EntityId.Value))
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

        var childErrors = failedParentIds.Count == 0
            ? []
            : await db.ProcessingJobs
                .AsNoTracking()
                .Where(j => j.ParentJobId != null
                            && failedParentIds.Contains(j.ParentJobId.Value)
                            && j.Status == ProcessingJobStatus.Failed
                            && j.Error != null)
                .Select(j => new { ParentJobId = j.ParentJobId!.Value, j.Error })
                .ToListAsync(ct);

        var errorsByParent = childErrors
            .GroupBy(e => e.ParentJobId)
            .ToDictionary(
                g => g.Key,
                // Capped: a large run can fail many batches, and the admin page needs the shape of the
                // problem, not every line of it. /admin/jobs remains the exhaustive record.
                g => (IReadOnlyList<string>)g.Select(e => e.Error!).Take(MaxChildErrors).ToList());

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
            BatchErrors = errorsByParent.GetValueOrDefault(r.Id, []),
            Summary = DeserializeSummary(r.Result),
        });
    }

    private const int MaxChildErrors = 5;

    // The staged result is handler-written jsonb. A shape change across a deploy would leave older rows
    // undeserializable; degrade to "no summary" rather than failing the whole admin page.
    private static BackupRunSummary? DeserializeSummary(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<BackupRunSummary>(result);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
