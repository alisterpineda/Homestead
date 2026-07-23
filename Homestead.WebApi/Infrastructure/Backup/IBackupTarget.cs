namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Identifies one object in the mirror. <paramref name="RelativePath"/> is the planner's forward-slash
/// path from the target root; <paramref name="Handle"/> is an opaque per-target identifier persisted on
/// the state row.
/// </summary>
/// <remarks>
/// The handle exists because not every destination addresses files by path. The local target returns the
/// relative path itself; a Drive target would return a file id, so a rename there is a metadata patch
/// rather than a path-to-path move, and a delete needs no path at all. Persisting whatever the target
/// hands back keeps the planner destination-agnostic.
/// </remarks>
public readonly record struct BackupObjectRef(string RelativePath, string? Handle);

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

    /// <summary>True when the target's config is complete and coherent enough to attempt writes.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason the target cannot run, or null when it can.</summary>
    string? ConfigurationError { get; }

    BackupTargetCapabilities Capabilities { get; }

    /// <summary>
    /// Writes (or overwrites) the object at <paramref name="target"/>. Returns the handle to persist.
    /// Implementations must be atomic from a reader's perspective — a crash mid-write must not leave a
    /// truncated file where a complete one used to be.
    /// </summary>
    Task<string> PutAsync(BackupObjectRef target, Stream content, string contentType, CancellationToken ct);

    /// <summary>Relocates an existing object. Returns the (possibly new) handle to persist.</summary>
    Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct);

    /// <summary>Removes an object. Must be idempotent — an already-absent object is not an error.</summary>
    Task DeleteAsync(BackupObjectRef target, CancellationToken ct);
}
