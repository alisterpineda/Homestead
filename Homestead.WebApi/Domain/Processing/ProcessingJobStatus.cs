namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingJobStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartiallyCompleted,

    /// <summary>
    /// Terminal. Two producers:
    /// <list type="bullet">
    /// <item>the backup source delete flow, which cancels a live run before removing the row it belongs to;</item>
    /// <item><c>JobRunner.FailAndCancelDependentsAsync</c>, which cancels a failed child's still-pending
    /// siblings at strictly lower priority — the ones whose happens-before edge says they depended on it.
    /// Only on the child's final failure: a child that will be retried stays Pending and keeps blocking them.</item>
    /// </list>
    /// Either way the job is never picked up again: the claim takes only <see cref="Pending"/> rows and
    /// <see cref="InProgress"/> ones whose lease has expired. And
    /// <c>UpdateParentProgressAsync</c> tallies only Completed/Failed, so a Cancelled child neither counts
    /// toward a parent's terminal derivation nor blocks it — the processor keeps the parent finalizable by
    /// decrementing <c>TotalChildCount</c> when it cancels, not by counting cancellations.
    /// </summary>
    /// <remarks>
    /// Stored as a string, so its position in this enum carries no meaning and adding it here does not
    /// renumber anything.
    /// </remarks>
    Cancelled
}
