namespace Homestead.WebApi.Processing;

public class JobProcessingOptions
{
    public const string SectionName = "Processing";

    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// How long one job may run before the processor cancels it.
    /// </summary>
    /// <remarks>
    /// Bound rather than hard-coded because a handler now needs to <i>read</i> it: the backup batch
    /// handler paces itself against this deadline, stopping with a safety margin and continuing under a
    /// fresh job instead of being killed mid-upload. A handler that computed its own margin against a
    /// constant would silently drift the moment this changed.
    /// </remarks>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // Floor: a zero or negative timeout would cancel every job before it started.
    public TimeSpan ResolveJobTimeout() =>
        JobTimeout > TimeSpan.Zero ? JobTimeout : TimeSpan.FromMinutes(5);
}
