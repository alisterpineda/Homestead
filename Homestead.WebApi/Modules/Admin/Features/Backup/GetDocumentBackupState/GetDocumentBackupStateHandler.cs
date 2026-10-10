using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetDocumentBackupState;

/// <summary>
/// Answers "where did <i>this</i> document go in the mirror, and is that backup even alive" — a question
/// <c>/admin/backup</c> cannot, because it reports at the pairing level.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order of the facts is the design.</b> Liveness comes first (is the pairing reachable, when did
/// it last actually mirror something) and the copy record is subordinate to it, because the mirror
/// verifies nothing: every value here is a record of what was written, never evidence that it is still
/// there. Disconnecting a target deliberately keeps its state rows, so a panel that led with the copy
/// record would render a cheerful "backed up" against a destination nobody can reach.
/// </para>
/// <para>
/// Returns counts, timestamps, paths and reasons — never a status word. The phrasing is the client's job,
/// which is also what lets verify runs add fields later instead of breaking a vocabulary.
/// </para>
/// </remarks>
public class GetDocumentBackupStateHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IBackupTargetGate gate,
    IBackupSourceCatalog catalog,
    IBackupRunHistory runHistory)
{
    public async Task<GetDocumentBackupStateResponse?> Handle(
        GetDocumentBackupStateQuery request, CancellationToken ct)
    {
        // Explicit, because DocumentAuthorizationHandler short-circuits for admins and would never reject
        // this: without the check, asking about a document that does not exist would render an empty
        // panel rather than a 404. The global soft-delete filter applies, so a trashed document 404s here
        // exactly as GET /documents/{id} does — reaching the mirror record of something in the trash is a
        // known gap, not an oversight.
        var documentExists = await db.Documents
            .AsNoTracking()
            .AnyAsync(d => d.Id == request.DocumentId, ct);

        if (!documentExists)
            return null;

        var versions = await db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.DocumentId == request.DocumentId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new LiveVersion(v.Id, v.VersionNumber))
            .ToListAsync(ct);

        var sources = await db.BackupSources
            .AsNoTracking()
            .Where(s => s.SourceKind == DocumentsBackupSourceDescriptor.SourceKey)
            .OrderBy(s => s.TargetKey)
            .ToListAsync(ct);

        if (sources.Count == 0)
        {
            return new GetDocumentBackupStateResponse
            {
                DocumentId = request.DocumentId,
                LiveVersionCount = versions.Count,
                Sources = [],
            };
        }

        var sourceIds = sources.Select(s => s.Id).ToList();

        // One row per configured destination at most, so loading the lot beats a key filter that would
        // have to reproduce the resolver's case-insensitive key semantics in SQL.
        var targetStates = await db.BackupTargetStates
            .AsNoTracking()
            .ToDictionaryAsync(s => s.TargetKey, StringComparer.OrdinalIgnoreCase, ct);

        // One gate check per distinct TARGET, not per pairing: a Drive check is a database read plus a
        // Data-Protection unprotect, and two documents pairings on one destination share an answer.
        var gateResults = new Dictionary<string, GateResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in sources
            .Select(s => s.TargetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // For a configured key this is the real target; for a dangling one the resolver's
            // unavailable-with-reason stand-in, which is exactly what the panel should say.
            var target = resolver.Resolve(key);
            gateResults[key] = new GateResult(target, await gate.CheckAsync(target, ct));
        }

        // Drives the (BackupSourceId, DocumentId) index. NOT filtered by DocumentId alone — no index
        // leads with it, so that shape would seq-scan the whole state table.
        var stateRows = await db.DocumentBackupStates
            .AsNoTracking()
            .Where(s => sourceIds.Contains(s.BackupSourceId) && s.DocumentId == request.DocumentId)
            .ToListAsync(ct);

        var statesBySource = stateRows
            .GroupBy(s => s.BackupSourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lastRuns = await runHistory.GetLastRunsAsync(sourceIds, ct);
        var lastSuccesses = await runHistory.GetLastSuccessfulRunsAsync(sourceIds, ct);
        var schedules = await runHistory.GetSchedulesAsync(
            [.. sources.Where(s => s.ScheduleId is not null).Select(s => s.ScheduleId!.Value)], ct);

        var failuresBySource = await LoadActionFailuresAsync(sources, lastRuns, ct);

        // Built once, not per pairing: both per-source counts below are set membership against the live
        // version ids, and re-scanning the version list for every state row turns a two-line roll-up into
        // a quadratic walk on a document with a deep version history.
        var liveVersionIds = versions.Select(v => v.Id).ToHashSet();

        var items = new List<DocumentBackupSourceState>();

        foreach (var source in sources)
        {
            var gateResult = gateResults[source.TargetKey];
            var descriptor = catalog.Find(source.SourceKind);

            // Two independent ways a pairing can be unrunnable, reported as one reason because the reader's
            // next move is the same either way: fix the deployment or delete the pairing.
            var unavailableReason = descriptor is null
                ? $"Backup source kind '{source.SourceKind}' is no longer available in this build."
                : gateResult.Verdict.IsAvailable
                    ? null
                    : gateResult.Verdict.Reason;

            var isAvailable = unavailableReason is null;

            var sourceStates = statesBySource.GetValueOrDefault(source.Id, []);
            var stateByVersion = sourceStates.ToDictionary(s => s.DocumentVersionId);

            var lastRun = lastRuns.GetValueOrDefault(source.Id);
            var failures = failuresBySource.GetValueOrDefault(source.Id, []);

            // The run that recorded the failure is the only clock available: BackupActionFailure carries
            // no timestamp of its own, and the batch child that staged it is not read here.
            var failedAt = lastRun?.CompletedAt ?? lastRun?.CreatedAt;

            targetStates.TryGetValue(source.TargetKey, out var targetState);

            items.Add(new DocumentBackupSourceState
            {
                BackupSourceId = source.Id,
                TargetKey = gateResult.Target.Key,
                TargetKind = gateResult.Target.Kind,
                SourceDisplayName = descriptor?.DisplayName,
                IsAvailable = isAvailable,
                UnavailableReason = unavailableReason,
                IsEnabled = source.IsEnabled,
                // No row means "never touched by an operator", which reads as enabled: defining the
                // target in config is the opt-in.
                TargetEnabled = targetState?.IsEnabled ?? true,
                TargetAccountEmail = targetState?.ConnectedAccountEmail,
                LastSuccessfulRunAt = lastSuccesses.TryGetValue(source.Id, out var lastSuccess)
                    ? lastSuccess
                    : null,
                LastRun = lastRun,
                Schedule = source.ScheduleId is { } scheduleId
                    ? schedules.GetValueOrDefault(scheduleId)
                    : null,
                // Intersected with the LIVE versions. State rows are FK-less tombstones that outlive the
                // version they describe, so counting rows would report "3 of 2" after a permanent delete.
                CopiedVersionCount = versions.Count(v => stateByVersion.ContainsKey(v.Id)),
                PendingRemovalCount = sourceStates.Count(s =>
                    !liveVersionIds.Contains(s.DocumentVersionId)),
                Versions = [.. versions.Select(version => BuildVersion(
                    version,
                    stateByVersion.GetValueOrDefault(version.Id),
                    failures.GetValueOrDefault(version.Id),
                    failedAt,
                    gateResult.Target,
                    isAvailable))],
            });
        }

        return new GetDocumentBackupStateResponse
        {
            DocumentId = request.DocumentId,
            LiveVersionCount = versions.Count,
            Sources = items,
        };
    }

    private static DocumentBackupVersionState BuildVersion(
        LiveVersion version,
        DocumentBackupState? state,
        BackupActionFailure? failure,
        DateTimeOffset? failedAt,
        IBackupTarget target,
        bool isAvailable) => new()
        {
            DocumentVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            LastWrittenAt = state?.BackedUpAt,
            RemoteRelativePath = state?.RemoteRelativePath,
            // Only while the pairing is usable. A link built from a handle recorded before a disconnect
            // (or before someone reconnected a different account) points at something the reader cannot
            // open, which is worse than offering no link at all.
            RemoteUrl = isAvailable ? target.DescribeObjectUrl(state?.RemoteHandle) : null,
            FileSize = state?.FileSize,
            LastFailureMessage = failure?.Message,
            LastFailureAt = failure is null ? null : failedAt,
        };

    /// <summary>
    /// The per-version failures recorded by each pairing's newest run, for the runs that had any.
    /// </summary>
    /// <remarks>
    /// Only asked for when the run reports failed children, so a healthy instance issues none of these
    /// queries. The job id comes straight off the run status, so there is no second newest-parent lookup.
    /// </remarks>
    private async Task<Dictionary<Guid, Dictionary<Guid, BackupActionFailure>>> LoadActionFailuresAsync(
        List<BackupSource> sources,
        Dictionary<Guid, BackupRunStatus> lastRuns,
        CancellationToken ct)
    {
        var bySource = new Dictionary<Guid, Dictionary<Guid, BackupActionFailure>>();

        foreach (var source in sources)
        {
            if (lastRuns.GetValueOrDefault(source.Id) is not { FailedChildCount: > 0 } run)
                continue;

            var failures = await runHistory.GetActionFailuresAsync(run.JobId, ct);

            if (failures.Count == 0)
                continue;

            // Last wins: a run can retry the same version across a continuation batch, and the newest
            // attempt is the one that describes where it stands now.
            bySource[source.Id] = failures
                .GroupBy(f => f.VersionId)
                .ToDictionary(g => g.Key, g => g.Last());
        }

        return bySource;
    }

    private readonly record struct LiveVersion(Guid Id, int VersionNumber);

    private readonly record struct GateResult(IBackupTarget Target, TargetGateResult Verdict);
}
