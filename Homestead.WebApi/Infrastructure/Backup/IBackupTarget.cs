namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Identifies one object in the mirror. <paramref name="RelativePath"/> is the planner's forward-slash
/// path from the target root; <paramref name="Handle"/> is an opaque per-target identifier persisted on
/// the state row.
/// </summary>
/// <remarks>
/// The handle exists because not every destination addresses files by path. The local target returns the
/// relative path itself; the Drive target returns a file id, so a rename there is a metadata patch
/// rather than a path-to-path move, and a delete needs no path at all. Persisting whatever the target
/// hands back keeps the planner destination-agnostic.
/// </remarks>
public readonly record struct BackupObjectRef(string RelativePath, string? Handle);

/// <summary>
/// Everything a <c>Put</c> needs beyond the bytes themselves.
/// </summary>
/// <param name="ContentType">Best-effort MIME type from the destination extension.</param>
/// <param name="Sha256Hex">
/// The content hash the caller already verified the source blob against. A target that reports its own
/// checksum on the write response compares against this, which is how a copy is verified without ever
/// being downloaded back.
/// </param>
/// <param name="Md5Hex">
/// MD5 of the content, computed alongside the pre-write SHA-256 in the same pass. Only a fallback: Drive
/// documents <c>sha256Checksum</c> as "if available", and when it is absent this is the one thing left to
/// verify the upload against without downloading it back.
/// </param>
/// <param name="ResumeSessionUri">
/// A parked resumable session to continue, or null to start fresh. Set when a previous batch ran out of
/// budget mid-file.
/// </param>
/// <param name="ResumeOffset">Bytes the destination already acknowledged for <paramref name="ResumeSessionUri"/>.</param>
/// <param name="Deadline">
/// When the caller's budget expires. A target that can park an upload stops sending chunks at this point
/// and returns <see cref="BackupPutResult.Suspended"/> — the difference between "the run is converging in
/// windows" and a cancellation, which is a failure. Null means no budget: run to completion.
/// </param>
public readonly record struct BackupPutContext(
    string ContentType,
    string? Sha256Hex = null,
    string? Md5Hex = null,
    string? ResumeSessionUri = null,
    long ResumeOffset = 0,
    DateTimeOffset? Deadline = null);

/// <summary>The outcome of a <c>Put</c>: either the object landed, or it is parked mid-flight.</summary>
public abstract record BackupPutResult
{
    private BackupPutResult() { }

    /// <summary>The object is fully written. <paramref name="Handle"/> is what the state row records.</summary>
    public sealed record Completed(string Handle) : BackupPutResult;

    /// <summary>
    /// The budget ran out mid-upload. Nothing is claimed as mirrored — no state row is written — but the
    /// session survives so a continuation batch resumes at <paramref name="BytesCommitted"/> instead of
    /// byte zero. Without this, a file larger than one budget window could never be mirrored at all.
    /// </summary>
    public sealed record Suspended(string SessionUri, long BytesCommitted) : BackupPutResult;
}

/// <summary>
/// A destination the document mirror can be written to. One-way only: the mirror is reproduced FROM
/// Homestead, never read back into it, so there is deliberately no list/read/download member — nothing
/// in the design has any reason to trust what is out there over what the database says.
/// </summary>
public interface IBackupTarget
{
    /// <summary>The config key ("Backup:Targets:{key}") this instance was defined under.</summary>
    string Key { get; }

    BackupTargetKind? Kind { get; }

    /// <summary>Where the mirror lives, for display on the admin page. Null when unconfigured.</summary>
    string? Location { get; }

    /// <summary>
    /// True when the target's <i>config</i> is complete and coherent enough to attempt writes. A static
    /// verdict computed once — whether a stored credential exists and still works is database state, so
    /// the composed answer every gate actually consults comes from <see cref="IBackupTargetGate"/>.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason the target's config cannot run, or null when it can.</summary>
    string? ConfigurationError { get; }

    BackupTargetCapabilities Capabilities { get; }

    /// <summary>
    /// How this destination wants work batched. Default is "no opinion" — the source kind's caps stand.
    /// </summary>
    BackupBatchCapOverrides BatchCapOverrides => default;

    /// <summary>
    /// Writes (or overwrites) the object at <paramref name="target"/>. Implementations must be atomic
    /// from a reader's perspective — a crash mid-write must not leave a truncated file where a complete
    /// one used to be.
    /// </summary>
    Task<BackupPutResult> PutAsync(
        BackupObjectRef target, Stream content, BackupPutContext context, CancellationToken ct);

    /// <summary>Relocates an existing object. Returns the (possibly new) handle to persist.</summary>
    Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct);

    /// <summary>Removes an object. Must be idempotent — an already-absent object is not an error.</summary>
    Task DeleteAsync(BackupObjectRef target, CancellationToken ct);

    /// <summary>
    /// Creates or verifies every folder in <paramref name="folderPaths"/> before any batch runs, so
    /// batches never resolve a folder themselves.
    /// </summary>
    /// <remarks>
    /// A no-op for a filesystem, where <c>Directory.CreateDirectory</c> at write time is race-free and
    /// free. It exists for id-addressed destinations, where concurrent find-or-create mints twin folders
    /// that split the mirror permanently — so folders are resolved once, sequentially, and remembered.
    /// This is the one stated exception to "the planner does no I/O": it is bounded by the number of NEW
    /// folders (tens), never by file count.
    /// </remarks>
    Task EnsureFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Removes any of <paramref name="folderPaths"/> that are now empty, walking up toward the root.
    /// Best-effort — a leftover empty folder is cosmetic.
    /// </summary>
    /// <remarks>
    /// The local target prunes inline after each delete/move and leaves this a no-op. A destination with
    /// an eventually-consistent listing cannot: an inline emptiness check races the same run's own moves,
    /// and could trash a folder with a just-arrived file inside it whose state row still reads "mirrored"
    /// — a silent hole the planner would never re-plan. Those targets get a final pass instead.
    /// </remarks>
    Task PruneEmptyFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct) =>
        Task.CompletedTask;
}
