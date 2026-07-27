namespace Homestead.WebApi.Modules.Documents.Backup;

/// <summary>
/// One document version as the planner sees it — a flat projection off <c>document_versions</c> joined to
/// its parent document. Deliberately narrow: the planner runs over every version in the instance, so
/// pulling whole entities (with <c>ExtractedText</c>) would be an order of magnitude more data than the
/// path calculation needs.
/// </summary>
/// <param name="SourceFilePath">
/// The media-root-relative path of the blob (<c>documents/files/{hash[..4]}/{hash}{ext}</c>). Carried
/// through from the stored column rather than re-derived from the hash, so the copy always reads the
/// location the upload actually wrote.
/// </param>
public record BackupVersionSource(
    Guid VersionId,
    Guid DocumentId,
    string DocumentName,
    DateOnly? DocumentDate,
    int VersionNumber,
    string FileHash,
    string FileExtension,
    string SourceFilePath,
    long FileSize);

/// <summary>Where one version should live in the mirror, as resolved by <see cref="BackupPathPlanner"/>.</summary>
public record BackupDesiredFile(
    Guid VersionId,
    Guid DocumentId,
    string RelativePath,
    string FileHash,
    string SourceFilePath,
    long FileSize);

/// <summary>
/// What the mirror is believed to already hold for one version — the projection of a
/// <c>DocumentBackupState</c> row that the plan builder diffs against.
/// </summary>
public record BackupStateRow(
    Guid VersionId,
    Guid DocumentId,
    string RelativePath,
    string? Handle,
    string BackedUpHash,
    long FileSize);

public enum BackupActionKind
{
    /// <summary>Copy the blob to <c>RelativePath</c>, replacing whatever is there.</summary>
    Put,

    /// <summary>Relocate an already-copied object from <c>FromRelativePath</c> to <c>RelativePath</c>.</summary>
    Move,

    /// <summary>Remove the object at <c>RelativePath</c> and drop its state row.</summary>
    Delete,

    /// <summary>
    /// Remove the folder at <c>RelativePath</c> if it is now empty, walking up toward the kind root.
    /// Best-effort and carries no state row.
    /// </summary>
    /// <remarks>
    /// An action rather than an inline step because on an eventually-consistent destination an emptiness
    /// check racing the same run's own moves could trash a folder with a just-arrived file inside it —
    /// whose state row would still read "mirrored", a silent hole nothing would ever re-plan. These are
    /// packed into one final batch, tagged with the lowest stage, that the queue will not start until
    /// every mutation batch in the run has terminated.
    /// </remarks>
    PruneFolder
}

/// <summary>
/// One unit of work for a <c>BackupBatch</c> child. Fully resolved by the planner — a child never
/// recomputes a path, because collision suffixes depend on global knowledge of the document set that a
/// single batch does not have.
/// </summary>
/// <remarks>
/// Serialized into the child job payload, so members are plain init-only with defaults rather than
/// <c>required</c>: an older payload sitting in the queue across a deploy must still deserialize.
/// </remarks>
public record BackupAction
{
    public BackupActionKind Kind { get; init; }

    public Guid VersionId { get; init; }
    public Guid DocumentId { get; init; }

    /// <summary>Destination path for <c>Put</c>/<c>Move</c>; the existing path for <c>Delete</c>.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Source path for a <c>Move</c>; null otherwise.</summary>
    public string? FromRelativePath { get; init; }

    /// <summary>The target's own identifier from the last successful write. Null for a first-time Put.</summary>
    public string? Handle { get; init; }

    /// <summary>Media-relative blob path to read for a <c>Put</c>; null otherwise.</summary>
    public string? SourceFilePath { get; init; }

    /// <summary>Expected SHA-256 of the blob, verified before a <c>Put</c> writes anything.</summary>
    public string FileHash { get; init; } = string.Empty;

    public long FileSize { get; init; }
}

/// <summary>
/// The pipeline position of a batch. A lower stage runs strictly before a higher one; batches sharing a
/// stage are mutually independent and run concurrently.
/// </summary>
/// <remarks>
/// The ordering contract made explicit at the type level rather than inferred from <c>Actions[0].Kind</c>
/// — which it cannot be anyway, since breaking a rename cycle puts a Delete in the Delete stage and a Put
/// in the Put stage that both came from a Move. <c>BackupToTargetJobHandler</c> maps this onto child job
/// priority, and the processor's claim predicate turns that into a real happens-before edge.
/// </remarks>
public enum BackupStage
{
    Delete = 0,
    Move = 1,
    Put = 2,
    Prune = 3
}

/// <summary>One child job's worth of actions, plus the pipeline stage that orders it.</summary>
/// <remarks>
/// <c>BackupBatchPayload</c> carries only <see cref="Actions"/>, so <see cref="Stage"/> never reaches the
/// serialized child payload — nothing sitting in the queue across a deploy has to know about it.
/// </remarks>
public record BackupActionBatch(BackupStage Stage, IReadOnlyList<BackupAction> Actions);

/// <summary>
/// The full change set for one run: batched work plus the counts the dry-run summary and the admin page
/// report.
/// </summary>
public record BackupPlan
{
    public IReadOnlyList<BackupActionBatch> Batches { get; init; } = [];

    public int PutCount { get; init; }
    public int MoveCount { get; init; }

    /// <summary>Deletes actually emitted — zero when the breaker latched.</summary>
    public int DeleteCount { get; init; }

    /// <summary>Deletes withheld by the breaker. Non-zero only when <see cref="PruneBlocked"/> is true.</summary>
    public int BlockedDeleteCount { get; init; }

    /// <summary>True when the delete count exceeded the breaker threshold and was withheld.</summary>
    public bool PruneBlocked { get; init; }

    /// <summary>Bytes to copy across every <c>Put</c>.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Versions already mirrored at the right path with the right content — no action emitted.</summary>
    public int UnchangedCount { get; init; }

    public int ActionCount => PutCount + MoveCount + DeleteCount;
}
