namespace Homestead.WebApi.Processing;

/// <summary>
/// A job ran past its budget — <see cref="JobProcessingOptions.JobTimeout"/>, or the one its handler
/// declared through <see cref="IJobHandler.ResolveTimeout"/>. Its own type, distinct from an
/// HttpClient's <see cref="TimeoutException"/>, because the two mean opposite things to a retry: a slow
/// upstream may well answer next time, but a handler that needs longer than the budget will need longer
/// than the budget again.
/// </summary>
public sealed class JobTimeoutException(Guid jobId, TimeSpan timeout)
    : TimeoutException($"Job {jobId} timed out after {Describe(timeout)}")
{
    public TimeSpan Timeout { get; } = timeout;

    // Whole minutes read as before ("5 minutes"); anything else as a TimeSpan, which an engine budget
    // like 00:00:20 or a test's 300 ms would otherwise render as a fraction of a minute.
    private static string Describe(TimeSpan timeout) =>
        timeout.Ticks % TimeSpan.TicksPerMinute == 0
            ? $"{timeout.TotalMinutes} minutes"
            : timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
}
