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

    /// <summary>
    /// How far ahead a claim, and each heartbeat after it, pushes a running job's lease.
    /// </summary>
    /// <remarks>
    /// The lease is how a <b>crashed process</b> is noticed: nothing else reclaims its work, so this is
    /// the longest an orphan waits. It is unrelated to <see cref="JobTimeout"/>, which caps a runaway
    /// handler in a process that is still alive — a five-minute job simply keeps renewing a two-minute
    /// lease.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often a running job's lease is pushed forward. Must be well under <see cref="LeaseDuration"/>.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest the processor sleeps between drains when no signal arrives. A safety net: an enqueue
    /// signals, and a retry or an orphaned lease coming due shortens the sleep to match.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay before each retry, indexed by how many attempts have failed; the last entry repeats.
    /// Null means <see cref="JobBackoff.DefaultSchedule"/>.
    /// </summary>
    /// <remarks>
    /// Null rather than pre-populated because the configuration binder <i>appends</i> to an existing
    /// array: a default here plus a configured schedule would silently run both.
    /// </remarks>
    public TimeSpan[]? RetryBackoff { get; set; }

    // Floor: a zero or negative timeout would cancel every job before it started.
    public TimeSpan ResolveJobTimeout() =>
        JobTimeout > TimeSpan.Zero ? JobTimeout : TimeSpan.FromMinutes(5);

    public IReadOnlyList<TimeSpan> ResolveRetryBackoff() =>
        RetryBackoff is { Length: > 0 } configured ? configured : JobBackoff.DefaultSchedule;
}
