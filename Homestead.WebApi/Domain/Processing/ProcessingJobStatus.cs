namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingJobStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartiallyCompleted,

    /// <summary>
    /// Terminal. Set only by the backup source delete flow, which cancels a live run before removing the
    /// row it belongs to. Nothing in the processor ever produces this: the claim query picks only
    /// <see cref="Pending"/>, so a job flipped to Cancelled is never picked up, and
    /// <c>JobProcessorService.UpdateParentProgressAsync</c> tallies only Completed/Failed, so a Cancelled
    /// child neither counts toward a parent's terminal derivation nor blocks it.
    /// </summary>
    /// <remarks>
    /// Stored as a string, so its position in this enum carries no meaning and adding it here does not
    /// renumber anything.
    /// </remarks>
    Cancelled
}
