namespace Homestead.WebApi.Processing;

/// <summary>
/// A job ran past <see cref="JobProcessingOptions.JobTimeout"/>. Its own type, distinct from an
/// HttpClient's <see cref="TimeoutException"/>, because the two mean opposite things to a retry: a slow
/// upstream may well answer next time, but a handler that needs longer than the budget will need longer
/// than the budget again.
/// </summary>
public sealed class JobTimeoutException(Guid jobId, TimeSpan timeout)
    : TimeoutException($"Job {jobId} timed out after {timeout.TotalMinutes} minutes");
