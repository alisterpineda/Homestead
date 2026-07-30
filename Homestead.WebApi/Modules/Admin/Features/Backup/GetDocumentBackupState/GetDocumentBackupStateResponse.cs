using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetDocumentBackupState;

/// <summary>
/// What one document version's copy in one mirror actually records.
/// </summary>
/// <remarks>
/// Every field here is a record of what <i>we wrote</i>, never evidence that it is still there — the
/// mirror is one-way and nothing reads it back. That is why there is no "verified" and no hash
/// comparison: <c>DocumentVersion.FileHash</c> is written once at insert and never rewritten, so
/// comparing it against <c>BackedUpHash</c> could only ever agree with itself.
/// </remarks>
public record DocumentBackupVersionState
{
    public required Guid DocumentVersionId { get; init; }
    public required int VersionNumber { get; init; }

    /// <summary>
    /// When the mirror last <i>wrote</i> this version — not necessarily when it last copied it.
    /// </summary>
    /// <remarks>
    /// Named for what it is because a rename bumps this without moving a byte: on an id-addressed
    /// destination a move is a metadata patch, so "copied 2 minutes ago" after a rename would be a copy
    /// event that never happened.
    /// </remarks>
    public DateTimeOffset? LastWrittenAt { get; init; }

    /// <summary>Forward-slash path from the target root, as last written. Null when never copied.</summary>
    public string? RemoteRelativePath { get; init; }

    /// <summary>
    /// A browsable link to the object, for destinations that have one. Emitted only while the pairing is
    /// available — a dead link after a disconnect or an account switch is worse than no link.
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>Bytes recorded for the copy. Null when nothing has been copied.</summary>
    public long? FileSize { get; init; }

    /// <summary>
    /// This version's own failure from the last run, when one was recorded against it.
    /// </summary>
    /// <remarks>
    /// Null is genuinely ambiguous and the client must word it that way: it means "no failure was
    /// recorded for this version", which covers both "nothing went wrong" and "the run failed and did not
    /// say which files". Older batch rows staged no per-action list at all.
    /// </remarks>
    public string? LastFailureMessage { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }
}

/// <summary>
/// One pairing's answer for this document: is the mirror alive, and what does it record about the
/// document's versions.
/// </summary>
/// <remarks>
/// Deliberately counts, timestamps, paths, and reasons rather than a status word. A status enum would
/// have to be re-specified the moment verify runs land, and — more importantly — any single word green
/// enough to be reassuring would claim something the mirror cannot support. Availability comes first for
/// the same reason: disconnecting a target keeps its state rows, so without the gate verdict every
/// document would render a cheerful copy record against a destination nobody can reach.
/// </remarks>
public record DocumentBackupSourceState
{
    public required Guid BackupSourceId { get; init; }
    public required string TargetKey { get; init; }
    public BackupTargetKind? TargetKind { get; init; }

    /// <summary>The catalog's label for the source kind, or null when it is no longer registered.</summary>
    public string? SourceDisplayName { get; init; }

    /// <summary>
    /// The composed gate verdict — config, and for a connectable kind the stored credential too. This is
    /// the first thing the panel renders, because it is the only field that can invalidate every other.
    /// </summary>
    public required bool IsAvailable { get; init; }

    public string? UnavailableReason { get; init; }

    /// <summary>The operator toggle on the pairing.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>The operator toggle on the whole destination. True when no row has ever been written.</summary>
    public required bool TargetEnabled { get; init; }

    /// <summary>
    /// Which account the destination is connected as, for kinds that connect. Labels the object link so
    /// "which Drive is this in?" is answerable without opening it.
    /// </summary>
    public string? TargetAccountEmail { get; init; }

    /// <summary>
    /// When this pairing last finished a run that mirrored something. Null when it never has. The
    /// liveness fact the panel leads with — a copy record beside a months-old success is a stale mirror.
    /// </summary>
    public DateTimeOffset? LastSuccessfulRunAt { get; init; }

    public BackupRunStatus? LastRun { get; init; }
    public BackupScheduleSummary? Schedule { get; init; }

    /// <summary>
    /// How many of the document's <i>live</i> versions have a copy recorded.
    /// </summary>
    /// <remarks>
    /// Intersected with the live version ids rather than counted off the state table: those rows are
    /// FK-less tombstones that outlive the version they describe, so a plain count reads "3 of 2".
    /// </remarks>
    public required int CopiedVersionCount { get; init; }

    /// <summary>
    /// State rows for versions that no longer exist. Those files are still sitting in the mirror until
    /// the next run's anti-join removes them — or forever, if the delete breaker has latched.
    /// </summary>
    public required int PendingRemovalCount { get; init; }

    public IReadOnlyList<DocumentBackupVersionState> Versions { get; init; } = [];
}

/// <summary>
/// Where one document stands in every mirror configured to carry it.
/// </summary>
/// <remarks>
/// Read-only by design. Per-document selection is out of scope for the mirror — a backup that copies a
/// hand-picked subset is not a backup — so the only action this surface offers is a link to the pairing
/// that owns the schedule.
/// </remarks>
public record GetDocumentBackupStateResponse
{
    public required Guid DocumentId { get; init; }

    /// <summary>Versions that currently exist, which is what <c>CopiedVersionCount</c> is measured against.</summary>
    public required int LiveVersionCount { get; init; }

    /// <summary>
    /// The document-kind pairings, in stable target order. Empty when nothing is configured to back
    /// documents up, in which case the client renders nothing at all rather than an empty section.
    /// </summary>
    public IReadOnlyList<DocumentBackupSourceState> Sources { get; init; } = [];
}
