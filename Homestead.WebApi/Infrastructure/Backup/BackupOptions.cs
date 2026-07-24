namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The "Backup" config section: the set of named targets, and nothing else. Targets are
/// environment-bound facts (a filesystem path today, cloud credentials later), which is why they stay in
/// static config while every operational choice — which datasets go where, schedules, enablement —
/// lives in <c>backup.sources</c> rows.
/// </summary>
/// <remarks>
/// Engine knobs are per-source-kind and live under that kind's own section (see
/// <c>DocumentBackupEngineOptions</c>, <c>Backup:Documents</c>) — the right batch caps and breaker
/// thresholds depend on what is being mirrored, not on the destination.
/// </remarks>
public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>One entry per "Backup:Targets:{key}" child. Empty = backup off, the graceful no-op.</summary>
    public Dictionary<string, BackupTargetOptions> Targets { get; set; } = [];
}
