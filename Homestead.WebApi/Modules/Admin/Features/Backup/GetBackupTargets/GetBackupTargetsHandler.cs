using System.Text.Json;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

/// <summary>
/// Assembles the admin backup page: targets from config (via DI), the pairings written to each of them
/// from <c>backup.sources</c>, per-pairing copy counts from the per-kind state table, last-run status
/// derived from <c>processing.jobs</c>, and schedules joined by the source row's own schedule id.
/// </summary>
public class GetBackupTargetsHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IBackupTargetGate gate,
    IBackupTargetOptionsAccessor options,
    IBackupSourceCatalog catalog)
    : IRequestHandler<GetBackupTargetsQuery, GetBackupTargetsResponse>
{
    public async Task<GetBackupTargetsResponse> Handle(
        GetBackupTargetsQuery request, CancellationToken ct)
    {
        var sources = await db.BackupSources
            .AsNoTracking()
            .ToListAsync(ct);

        var targetStates = await db.BackupTargetStates
            .AsNoTracking()
            .ToDictionaryAsync(s => s.TargetKey, StringComparer.OrdinalIgnoreCase, ct);

        // Per-pairing mirrored totals. Grouped over the documents state table because that is the only
        // per-kind table today; when a second kind lands this becomes a hook on the descriptor rather
        // than a second query bolted on here. A pairing of another kind simply reports zero until then.
        var totals = await db.DocumentBackupStates
            .AsNoTracking()
            .GroupBy(s => s.BackupSourceId)
            .Select(g => new SourceTotals(g.Key, g.Count(), g.Sum(s => s.FileSize)))
            .ToListAsync(ct);

        var totalsBySource = totals.ToDictionary(t => t.SourceId);

        var lastRuns = await LoadLastRunsAsync(sources, ct);
        var lastSuccesses = await LoadLastSuccessfulRunsAsync(sources, ct);
        var schedules = await LoadSchedulesAsync(sources, ct);

        var sourcesByTarget = sources
            .GroupBy(s => s.TargetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var items = new List<BackupTargetResponse>();

        foreach (var key in CardKeys(sources))
        {
            // For a configured key this is the real target; for a stale one the resolver's
            // unavailable-with-reason stand-in, which is exactly what the card should say. A stale card is
            // never hidden — a pairing that has silently stopped backing something up must stay visible.
            var target = resolver.Resolve(key);

            targetStates.TryGetValue(key, out var state);

            // The composed verdict, not the config-shape one: a Drive target with perfect config and no
            // stored grant is not available, and the card has to say which.
            var verdict = await gate.CheckAsync(target, ct);

            var targetSources = sourcesByTarget.GetValueOrDefault(key, [])
                .OrderBy(s => s.SourceKind, StringComparer.OrdinalIgnoreCase)
                .Select(source =>
                    BuildSource(source, verdict, totalsBySource, lastRuns, lastSuccesses, schedules))
                .ToList();

            items.Add(new BackupTargetResponse
            {
                Key = target.Key,
                Kind = target.Kind,
                // The persisted root wins; config is the fallback only while nothing has been created.
                // Same shape as overriding the config-only IsAvailable with the composed verdict below —
                // what the card shows has to be the answer that is currently true, not the static one.
                Location = target.DescribeLocation(state?.RootFolderPath),
                LocationUrl = state?.RootFolderWebViewLink,
                LocationNotice = BuildLocationNotice(target, options.Get(key), state),
                IsAvailable = verdict.IsAvailable,
                ConfigurationError = verdict.Reason,
                Connection = BuildConnection(target, state),
                // No row means "never configured by an operator", which reads as enabled: defining a
                // target in config is the opt-in, and requiring a second in-app toggle would make a
                // freshly-deployed schedule silently do nothing.
                IsEnabled = state?.IsEnabled ?? true,
                Sources = targetSources,
            });
        }

        return new GetBackupTargetsResponse
        {
            Items = items,
            SourceKinds = [.. catalog.All().Select(d => new BackupSourceKindResponse
            {
                Key = d.Key,
                DisplayName = d.DisplayName,
                SupportsPrune = d.SupportsPrune,
            })],
        };
    }

    // Config targets first, in resolver order, then any target key that survives only on a source row.
    private IEnumerable<string> CardKeys(List<BackupSource> sources)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in resolver.All())
            if (seen.Add(target.Key))
                yield return target.Key;

        var staleKeys = sources
            .Select(s => s.TargetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        foreach (var key in staleKeys)
            if (seen.Add(key))
                yield return key;
    }

    /// <summary>
    /// The credential state for a kind that has one. Built from the row alone — the whole point of
    /// storing the email and issue time as plain columns is that the card renders without decrypting.
    /// </summary>
    private static BackupTargetConnection? BuildConnection(IBackupTarget target, BackupTargetState? state)
    {
        if (target.Kind is not BackupTargetKind.GoogleDrive)
            return null;

        return new BackupTargetConnection
        {
            IsConnected = state?.EncryptedGrant is { Length: > 0 },
            ConnectedAccountEmail = state?.ConnectedAccountEmail,
            GrantIssuedAt = state?.GrantIssuedAt,
            AuthFailedAt = state?.AuthFailedAt,
            AuthFailedReason = state?.AuthFailedReason,
        };
    }

    /// <summary>
    /// Names the one skew reading the location from the database can otherwise hide: config asking for a
    /// root the mirror was not created at.
    /// </summary>
    /// <remarks>
    /// Reading from the database is what makes the displayed location true — but it also turns a config
    /// edit into a silent no-op, and a setting that does nothing while nothing anywhere says so is the same
    /// trap in a new place. The common case (config unset, default used) renders nothing.
    /// </remarks>
    private static string? BuildLocationNotice(
        IBackupTarget target, BackupTargetOptions bag, BackupTargetState? state)
    {
        if (target.Kind is not BackupTargetKind.GoogleDrive)
            return null;

        // A target whose config cannot run renders no location at all, and a notice about a location the
        // card is not showing reads as an orphaned sentence. Its ConfigurationError is the message that
        // matters there.
        if (target.DescribeLocation(state?.RootFolderPath) is null)
            return null;

        if (state?.RootFolderPath is not { Length: > 0 } actual
            || bag.RootFolderPath is not { Length: > 0 } raw)
            return null;

        var configured = raw.Trim();

        // Case-insensitively, because Drive matches names that way: "homestead/backup" against
        // "Homestead/Backup" is the same folder, not a relocation nobody performed.
        if (configured.Length == 0
            || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"Config asks for '{configured}', but the mirror was created at '{actual}' and stays " +
               "there — the recorded copies point into that folder. Reconnecting a different Google " +
               "account starts fresh from config.";
    }

    private BackupSourceItemResponse BuildSource(
        BackupSource source,
        TargetGateResult verdict,
        Dictionary<Guid, SourceTotals> totalsBySource,
        Dictionary<Guid, BackupRunStatus> lastRuns,
        Dictionary<Guid, DateTimeOffset> lastSuccesses,
        Dictionary<Guid, BackupScheduleSummary> schedules)
    {
        var descriptor = catalog.Find(source.SourceKind);

        // Two independent ways a pairing can be unrunnable, reported as one reason because the admin's
        // next move is the same either way: fix the deployment or delete the row. A stale kind also loses
        // its prune affordances — nothing knows whether this dataset ever deleted anything.
        var unavailableReason = descriptor is null
            ? $"Backup source kind '{source.SourceKind}' is no longer available in this build."
            : verdict.IsAvailable
                ? null
                : verdict.Reason;

        totalsBySource.TryGetValue(source.Id, out var totals);

        return new BackupSourceItemResponse
        {
            Id = source.Id,
            SourceKind = source.SourceKind,
            SourceDisplayName = descriptor?.DisplayName,
            IsEnabled = source.IsEnabled,
            IsAvailable = unavailableReason is null,
            UnavailableReason = unavailableReason,
            SupportsPrune = descriptor?.SupportsPrune ?? false,
            PruneBlockedAt = source.PruneBlockedAt,
            PruneBlockedCount = source.PruneBlockedCount,
            BackedUpFileCount = totals?.Count ?? 0,
            BackedUpBytes = totals?.Bytes ?? 0,
            LastRun = lastRuns.GetValueOrDefault(source.Id),
            // TryGetValue, not GetValueOrDefault: the dictionary's value type is non-nullable, so a
            // missing key would yield DateTimeOffset.MinValue — which widens silently into the nullable
            // response field and renders as "2055y ago" instead of "never".
            LastSuccessfulRunAt = lastSuccesses.TryGetValue(source.Id, out var lastSuccess)
                ? lastSuccess
                : null,
            Schedule = source.ScheduleId is { } scheduleId
                ? schedules.GetValueOrDefault(scheduleId)
                : null,
        };
    }

    private const int MaxChildErrors = 5;

    private async Task<Dictionary<Guid, BackupRunStatus>> LoadLastRunsAsync(
        List<BackupSource> sources, CancellationToken ct)
    {
        var jobTypes = catalog.All().Select(d => d.JobType).Distinct().ToList();

        if (sources.Count == 0 || jobTypes.Count == 0)
            return [];

        var sourceIds = sources.Select(s => s.Id).ToList();

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
                // Capped: a large run can fail many batches, and the admin page needs the shape of the
                // problem, not every line of it. /admin/jobs remains the exhaustive record.
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

    /// <summary>
    /// When each pairing last completed a run cleanly.
    /// </summary>
    /// <remarks>
    /// A separate query from the newest-run one on purpose: the two answer different questions, and the
    /// gap between them is the signal. "Last run: failed, 4 minutes ago" and "last successful run: 38
    /// days ago" is a backup that has been quietly broken for over a month, which no single row says.
    /// </remarks>
    private async Task<Dictionary<Guid, DateTimeOffset>> LoadLastSuccessfulRunsAsync(
        List<BackupSource> sources, CancellationToken ct)
    {
        var jobTypes = catalog.All().Select(d => d.JobType).Distinct().ToList();

        if (sources.Count == 0 || jobTypes.Count == 0)
            return [];

        var sourceIds = sources.Select(s => s.Id).ToList();

        var rows = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => jobTypes.Contains(j.Type)
                        && j.EntityId != null
                        && sourceIds.Contains(j.EntityId.Value)
                        && j.ParentJobId == null
                        && j.Status == ProcessingJobStatus.Completed
                        && j.CompletedAt != null)
            .GroupBy(j => j.EntityId!.Value)
            .Select(g => new { EntityId = g.Key, CompletedAt = g.Max(j => j.CompletedAt) })
            .ToListAsync(ct);

        return rows
            .Where(r => r.CompletedAt is not null)
            .ToDictionary(r => r.EntityId, r => r.CompletedAt!.Value);
    }

    // Joined by the source row's own ScheduleId. The old shape had to load every BackupToTarget schedule
    // and match a target key out of the jsonb payload in memory; the FK makes that a plain id lookup.
    private async Task<Dictionary<Guid, BackupScheduleSummary>> LoadSchedulesAsync(
        List<BackupSource> sources, CancellationToken ct)
    {
        var scheduleIds = sources
            .Where(s => s.ScheduleId is not null)
            .Select(s => s.ScheduleId!.Value)
            .Distinct()
            .ToList();

        if (scheduleIds.Count == 0)
            return [];

        var rows = await db.ProcessingSchedules
            .AsNoTracking()
            .Where(s => scheduleIds.Contains(s.Id))
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

    // The staged result is handler-written jsonb. A shape change across a deploy would leave older rows
    // undeserializable; degrade to "no summary" rather than failing the whole admin page.
    private static BackupRunSummary? DeserializeSummary(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BackupRunSummary>(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Same degrade-don't-fail posture as the summary above: an older row whose shape has since changed
    // simply reports no outcome rather than taking the whole admin page down.
    private static BackupBatchOutcome? DeserializeOutcome(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BackupBatchOutcome>(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private record SourceTotals(Guid SourceId, int Count, long Bytes);
}
