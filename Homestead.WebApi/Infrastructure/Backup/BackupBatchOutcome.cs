namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The staged result of one backup batch child, read back by the admin page.
/// </summary>
/// <remarks>
/// A <i>typed</i> marker rather than a message the frontend matches on. The card needs to render "Google
/// Drive is out of storage" once, not two hundred identical fragments, and it must decide that without
/// parsing error strings — which is also why a parent job's failure reason cannot carry it (a parent
/// whose children failed has no error string of its own).
/// </remarks>
/// <param name="ErrorCode">A stable code, or null when the batch failed for ordinary reasons.</param>
/// <param name="SkippedPuts">How many copies were abandoned because they could not possibly succeed.</param>
public record BackupBatchOutcome(string? ErrorCode = null, int SkippedPuts = 0)
{
    /// <summary>The destination is full. Deletes and moves in the same batch still ran.</summary>
    public const string OutOfSpaceCode = "outOfSpace";
}
