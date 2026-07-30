namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// One per-file failure inside a batch, kept as data rather than only as a fragment spliced into the
/// batch's exception message.
/// </summary>
/// <remarks>
/// The message alone cannot answer "did the copy of <i>this</i> document fail, and why" — a first-ever
/// copy that fails writes no state row, so there is no persisted path to join a document against, and the
/// joined message is truncated anyway. Carrying the version id makes the failure addressable by the one
/// key every per-document surface already has.
/// </remarks>
/// <param name="VersionId">The document version the action was for.</param>
/// <param name="Kind">The action kind, as its enum name — the same token the message renders.</param>
/// <param name="RelativePath">The destination path the action was targeting.</param>
/// <param name="Message">The destination's own wording, never reworded here.</param>
public record BackupActionFailure(Guid VersionId, string Kind, string RelativePath, string Message);

/// <summary>
/// The staged result of one backup batch child, read back by the admin page.
/// </summary>
/// <remarks>
/// <para>
/// A <i>typed</i> marker rather than a message the frontend matches on. The card needs to render "Google
/// Drive is out of storage" once, not two hundred identical fragments, and it must decide that without
/// parsing error strings — which is also why a parent job's failure reason cannot carry it (a parent
/// whose children failed has no error string of its own).
/// </para>
/// <para>
/// Every parameter is defaulted and must stay that way: System.Text.Json deserializes this through the
/// positional constructor, so a row staged before a parameter existed would throw on read instead of
/// degrading to "not recorded".
/// </para>
/// </remarks>
/// <param name="ErrorCode">A stable code, or null when the batch failed for ordinary reasons.</param>
/// <param name="SkippedPuts">How many copies were abandoned because they could not possibly succeed.</param>
/// <param name="Failures">
/// The per-action failures, capped. Null on a batch that staged an outcome before this existed — which is
/// why a reader must word its absence as "not recorded" rather than as "nothing failed".
/// </param>
/// <param name="FailureCount">
/// The untruncated total, so a reader can tell "not listed here" from "did not fail".
/// </param>
public record BackupBatchOutcome(
    string? ErrorCode = null,
    int SkippedPuts = 0,
    IReadOnlyList<BackupActionFailure>? Failures = null,
    int FailureCount = 0)
{
    /// <summary>The destination is full. Deletes and moves in the same batch still ran.</summary>
    public const string OutOfSpaceCode = "outOfSpace";
}
