namespace Homestead.WebApi.Processing;

/// <summary>How long a failed job waits before its next attempt.</summary>
/// <remarks>
/// Pure and static on purpose: the schedule is the part of the retry policy worth asserting on
/// directly, and keeping it free of the runner means doing so needs no database.
/// </remarks>
public static class JobBackoff
{
    public static readonly IReadOnlyList<TimeSpan> DefaultSchedule =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
    ];

    /// <summary>The delay after <paramref name="failedAttempts"/> failed attempts.</summary>
    /// <remarks>
    /// Clamped at both ends rather than validated: this runs on the failure path, where throwing over an
    /// out-of-range count would lose the real error. A handler allowing more attempts than the schedule
    /// has entries simply keeps the last delay.
    /// </remarks>
    public static TimeSpan ForAttempt(int failedAttempts, IReadOnlyList<TimeSpan>? schedule = null)
    {
        if (schedule is not { Count: > 0 })
            schedule = DefaultSchedule;

        return schedule[Math.Clamp(failedAttempts - 1, 0, schedule.Count - 1)];
    }
}
