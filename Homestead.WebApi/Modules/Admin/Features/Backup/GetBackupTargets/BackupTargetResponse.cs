using Homestead.WebApi.Infrastructure.Backup;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

/// <summary>One <c>backup.sources</c> pairing, as rendered under its target's card.</summary>
public record BackupSourceItemResponse
{
    public required Guid Id { get; init; }

    /// <summary>The persisted kind key — still returned when nothing registers it, so stale rows are nameable.</summary>
    public required string SourceKind { get; init; }

    /// <summary>The catalog's label, or null when the kind is no longer registered.</summary>
    public string? SourceDisplayName { get; init; }

    /// <summary>The operator toggle on this pairing.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// False when this pairing cannot run at all — an unregistered kind or an unusable target. Distinct
    /// from <see cref="IsEnabled"/>, which is a choice rather than a fault.
    /// </summary>
    public required bool IsAvailable { get; init; }

    public string? UnavailableReason { get; init; }

    /// <summary>Whether the breaker affordances mean anything here. False for an unregistered kind.</summary>
    public required bool SupportsPrune { get; init; }

    public DateTimeOffset? PruneBlockedAt { get; init; }
    public int? PruneBlockedCount { get; init; }

    public required int BackedUpFileCount { get; init; }
    public required long BackedUpBytes { get; init; }

    public BackupRunStatus? LastRun { get; init; }

    /// <summary>
    /// When this pairing last finished a run cleanly. Null when it never has.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="LastRun"/>, which is the newest run whatever its outcome. This is the
    /// catch-all for every way a backup dies silently — a disabled schedule, a target nobody reconnected,
    /// a run that has failed the same way for a month — none of which the newest-run badge alone makes
    /// obvious at a glance. A run that mirrored nothing does not count: a skipped run and a dry run both
    /// complete normally, so counting them would let a paused backup report a fresh success forever.
    /// </remarks>
    public DateTimeOffset? LastSuccessfulRunAt { get; init; }

    public BackupScheduleSummary? Schedule { get; init; }
}

/// <summary>
/// The credential half of a target that can be <i>connected</i> as well as configured. Null for kinds
/// that cannot be (a local directory has nothing to connect).
/// </summary>
/// <remarks>
/// Read straight off the target-state row — zero I/O, no Google call. Proving the credential still works
/// is the separate <c>validate</c> endpoint the page fires after render.
/// </remarks>
public record BackupTargetConnection
{
    public required bool IsConnected { get; init; }

    /// <summary>Which Google account, so "which one did I use?" is answerable months later.</summary>
    public string? ConnectedAccountEmail { get; init; }

    public DateTimeOffset? GrantIssuedAt { get; init; }

    /// <summary>Set once the authorization has been observed to fail. Cleared by reconnecting.</summary>
    public DateTimeOffset? AuthFailedAt { get; init; }

    public string? AuthFailedReason { get; init; }
}

public record BackupTargetResponse
{
    public required string Key { get; init; }
    public BackupTargetKind? Kind { get; init; }

    /// <summary>
    /// Where the mirror lives (the configured path for a local target; for Drive, the root folder as
    /// actually created).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>A browsable URL for the destination, when it has one. Null for a local directory.</summary>
    public string? LocationUrl { get; init; }

    /// <summary>
    /// An advisory about the location — set when config asks for a root path the mirror was not created
    /// at. A full sentence, like <see cref="ConfigurationError"/>: the reasoning belongs on this side.
    /// </summary>
    public string? LocationNotice { get; init; }

    /// <summary>False when config is missing or incoherent; see <see cref="ConfigurationError"/>.</summary>
    public required bool IsAvailable { get; init; }
    public string? ConfigurationError { get; init; }

    /// <summary>Operator toggle for the whole destination. True for a target that has never been touched.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>The stored-credential state, for kinds that have one. Null for a local directory.</summary>
    public BackupTargetConnection? Connection { get; init; }

    /// <summary>The pairings writing to this target, in stable kind order. May be empty.</summary>
    public required IReadOnlyList<BackupSourceItemResponse> Sources { get; init; }
}

/// <summary>One registered source kind, for the "Add backup" picker.</summary>
public record BackupSourceKindResponse
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required bool SupportsPrune { get; init; }
}

public record GetBackupTargetsResponse
{
    public required IReadOnlyList<BackupTargetResponse> Items { get; init; }

    /// <summary>
    /// The catalog, fed straight to the picker. Sent with the list rather than fetched separately so
    /// "what can I add here" needs no second round trip.
    /// </summary>
    public required IReadOnlyList<BackupSourceKindResponse> SourceKinds { get; init; }
}
