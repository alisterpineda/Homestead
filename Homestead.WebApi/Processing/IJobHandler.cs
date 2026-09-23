using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Processing;

public interface IJobHandler
{
    ProcessingJobType Type { get; }

    /// <summary>Does the work. Throwing fails this attempt; whether another follows is <see cref="ShouldRetry"/>'s call.</summary>
    Task HandleAsync(ProcessingJob job, CancellationToken ct);

    /// <summary>
    /// How many runs this job type gets in total. <b>Retries are opt-in:</b> the default of 1 is the
    /// original "a failure is terminal" contract, and it stays right for anything a user is watching
    /// inline or anything that is not safe to run twice.
    /// </summary>
    /// <remarks>
    /// Opting in is a promise that <see cref="HandleAsync"/> is idempotent — a retry re-runs it from the
    /// top with whatever an earlier attempt already committed still in place. Parents never retry once
    /// they have children, whatever this says.
    /// </remarks>
    int MaxAttempts => 1;

    /// <summary>
    /// Whether this failure is worth another attempt, when attempts remain. Consulted only for real
    /// failures: shutdown, a lost lease, and the job's own timeout never reach it.
    /// </summary>
    bool ShouldRetry(Exception exception) => JobFailureClassifier.IsTransient(exception);

    /// <summary>
    /// Called once, after the last attempt has failed and the row is already <c>Failed</c>, so a handler
    /// can mark the entity it was working on failed too. Never called for an attempt that will be retried.
    /// </summary>
    /// <remarks>
    /// Runs in a fresh scope, with a read-only snapshot of the failed row — changing <paramref name="job"/>
    /// persists nothing. Throwing here only logs: the job's outcome is already recorded. At most once: a
    /// crash between the Failed write and this call skips it.
    /// </remarks>
    Task OnFinallyFailedAsync(ProcessingJob job, Exception exception, CancellationToken ct) => Task.CompletedTask;
}
