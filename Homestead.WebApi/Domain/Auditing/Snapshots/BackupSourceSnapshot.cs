using Homestead.WebApi.Domain.Backup;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

/// <remarks>
/// <see cref="SourceKind"/> and <see cref="TargetKey"/> are here for completeness of the audit record
/// rather than because anything edits them — the pairing they name is fixed at insert, and changing it
/// would be a different backup, so it is a delete plus a create. <see cref="ScheduleId"/> is the
/// opposite case: nothing on the PATCH surface touches it, but arming or removing a recurring run does,
/// and "backups silently stopped running" is exactly the question an audit trail has to answer.
/// </remarks>
public record BackupSourceSnapshot : IAuditSnapshot<BackupSourceSnapshot, BackupSource>
{
    public required string SourceKind { get; init; }
    public required string TargetKey { get; init; }
    public required bool IsEnabled { get; init; }
    public Guid? ScheduleId { get; init; }
    public DateTimeOffset? PruneBlockedAt { get; init; }
    public int? PruneBlockedCount { get; init; }

    public static BackupSourceSnapshot From(BackupSource source) => new()
    {
        SourceKind = source.SourceKind,
        TargetKey = source.TargetKey,
        IsEnabled = source.IsEnabled,
        ScheduleId = source.ScheduleId,
        PruneBlockedAt = source.PruneBlockedAt,
        PruneBlockedCount = source.PruneBlockedCount,
    };

    public List<FieldChange> Diff(BackupSourceSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (SourceKind != other.SourceKind)
            changes.Add(new ScalarFieldChange("SourceKind", SourceKind, other.SourceKind));
        if (TargetKey != other.TargetKey)
            changes.Add(new ScalarFieldChange("TargetKey", TargetKey, other.TargetKey));
        if (IsEnabled != other.IsEnabled)
            changes.Add(new ScalarFieldChange("IsEnabled", IsEnabled.ToString(), other.IsEnabled.ToString()));
        if (ScheduleId != other.ScheduleId)
            changes.Add(new ScalarFieldChange("ScheduleId", ScheduleId?.ToString(), other.ScheduleId?.ToString()));
        if (PruneBlockedAt != other.PruneBlockedAt)
            changes.Add(new ScalarFieldChange("PruneBlockedAt", Format(PruneBlockedAt), Format(other.PruneBlockedAt)));
        if (PruneBlockedCount != other.PruneBlockedCount)
            changes.Add(new ScalarFieldChange(
                "PruneBlockedCount", PruneBlockedCount?.ToString(), other.PruneBlockedCount?.ToString()));

        return changes;
    }

    private static string? Format(DateTimeOffset? value) => value?.ToString("O");
}
