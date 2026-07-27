namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// A resumable upload that ran out of batch budget mid-file, parked so the continuation batch can resume
/// at the committed offset instead of restarting from byte zero.
/// </summary>
/// <remarks>
/// <para>
/// Without this, any file larger than one deadline window's worth of uplink could <i>never</i> be
/// mirrored — every attempt would restart from zero and be cut off at the same place, forever.
/// </para>
/// <para>
/// <b>These rows cannot live on <c>DocumentBackupState</c>.</b> That row exists only <i>after</i> a
/// successful copy, and its absence-until-success is a load-bearing invariant: the planner reads "no
/// state row" as "not mirrored". A half-uploaded file must not create one.
/// </para>
/// <para>
/// <see cref="FileHash"/> is stamped so a resumed session is provably about the same bytes. If the
/// version's content changed underneath (a re-upload reusing the version id), the session is discarded
/// and the put starts fresh rather than splicing two files together. <see cref="DocumentVersionId"/>
/// carries no foreign key for the same tombstone reason as <c>DocumentBackupState</c>.
/// </para>
/// </remarks>
public class BackupUploadSession
{
    public Guid Id { get; set; }

    /// <summary>The pairing this upload belongs to. Cascades with the pairing, like its state rows.</summary>
    public Guid BackupSourceId { get; set; }

    /// <summary>The version being uploaded. No FK — see the type remarks.</summary>
    public Guid DocumentVersionId { get; set; }

    /// <summary>The destination's resumable session URI. Valid for about a week on Drive.</summary>
    public required string SessionUri { get; set; }

    /// <summary>Bytes the destination has acknowledged; where the continuation picks up.</summary>
    public long BytesCommitted { get; set; }

    /// <summary>SHA-256 of the content this session is uploading. A mismatch invalidates the session.</summary>
    public required string FileHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
