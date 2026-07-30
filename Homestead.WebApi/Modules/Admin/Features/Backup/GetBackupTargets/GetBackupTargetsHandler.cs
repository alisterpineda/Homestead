using Homestead.WebApi.Domain.Backup;
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
    IBackupSourceCatalog catalog,
    IBackupRunHistory runHistory)
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

        var sourceIds = sources.Select(s => s.Id).ToList();
        var scheduleIds = sources
            .Where(s => s.ScheduleId is not null)
            .Select(s => s.ScheduleId!.Value)
            .ToList();

        var lastRuns = await runHistory.GetLastRunsAsync(sourceIds, ct);
        var lastSuccesses = await runHistory.GetLastSuccessfulRunsAsync(sourceIds, ct);
        var schedules = await runHistory.GetSchedulesAsync(scheduleIds, ct);

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

    private record SourceTotals(Guid SourceId, int Count, long Bytes);
}
