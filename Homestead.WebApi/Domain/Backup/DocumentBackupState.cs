namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// One row per (target, document version) ever copied to a mirror. The planner diffs against these rows
/// instead of listing the destination, which is what makes an unchanged run cost a single query and zero
/// file reads.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="DocumentVersionId"/> carries no foreign key, deliberately.</b> These rows are
/// tombstones that must <i>outlive</i> their target: when a version is permanently deleted, this row is
/// the only record of what to remove from the mirror. A cascade would erase the instruction along with
/// the reason for it, leaving an orphaned file in the backup forever. This follows the product rule that
/// provenance-style references which must outlive their target stay FK-less (as audit logs do), and it
/// is why deletions are found by anti-join rather than by cascade.
/// </para>
/// <para>
/// <see cref="RemoteRelativePath"/> and <see cref="RemoteHandle"/> are denormalized for the same reason:
/// you cannot delete what you cannot name, and after the version row is gone there is nothing left to
/// recompute the path from.
/// </para>
/// </remarks>
public class DocumentBackupState
{
    public Guid Id { get; set; }

    /// <summary>The <c>Backup:Targets:{key}</c> this copy lives in.</summary>
    public required string TargetKey { get; set; }

    /// <summary>The mirrored version. No FK — see the type remarks.</summary>
    public Guid DocumentVersionId { get; set; }

    /// <summary>The version's parent document, for per-document lookups after the version row is gone.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Forward-slash path from the target root, as last written.</summary>
    public required string RemoteRelativePath { get; set; }

    /// <summary>The target's own identifier for the object (a path locally, a file id on a cloud target).</summary>
    public string? RemoteHandle { get; set; }

    /// <summary>SHA-256 of the bytes actually copied. Drives replace-vs-skip on the next run.</summary>
    public required string BackedUpHash { get; set; }

    public long FileSize { get; set; }

    public DateTimeOffset BackedUpAt { get; set; }
}
