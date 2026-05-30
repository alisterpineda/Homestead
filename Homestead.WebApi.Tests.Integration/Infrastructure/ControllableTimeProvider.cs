namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// A <see cref="TimeProvider"/> that follows the system clock by default but can be pinned to a fixed
/// instant for the duration of a test. Time-sensitive assertions — notably the scheduler's "fire once,
/// then nothing due" property — depend on two reads of "now" observing the same instant. Under the real
/// clock those reads can straddle a cron boundary (e.g. midnight UTC for a daily cron), letting the
/// second tick re-fire and flaking the test. Pinning the clock collapses that drift window to zero.
/// </summary>
/// <remarks>
/// Thread-safe: the scheduler reads the clock from its own scope while a test pins it from the test
/// thread, and async continuations may resume on different threads, so the frozen instant is published
/// via <see cref="Interlocked"/> to guarantee visibility.
/// </remarks>
public sealed class ControllableTimeProvider : TimeProvider
{
    private const long NotFrozen = long.MinValue;

    private long _frozenUtcTicks = NotFrozen;

    public override DateTimeOffset GetUtcNow()
    {
        var ticks = Interlocked.Read(ref _frozenUtcTicks);
        return ticks == NotFrozen
            ? System.GetUtcNow()
            : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// Pins "now" to the current system instant until the returned token is disposed. Nesting is not
    /// supported — a single test owns the clock for the scope of one <c>using</c> block. Self-enforced:
    /// freezing while already frozen throws, so a stray nested freeze can't silently leave the clock
    /// unfrozen (which would re-open the wall-clock-drift flake this class exists to close).
    /// </summary>
    public IDisposable Freeze()
    {
        var nowTicks = System.GetUtcNow().UtcTicks;
        if (Interlocked.CompareExchange(ref _frozenUtcTicks, nowTicks, NotFrozen) != NotFrozen)
            throw new InvalidOperationException(
                "The clock is already frozen. ControllableTimeProvider does not support nested freezes — " +
                "dispose the outstanding freeze token before pinning the clock again.");
        return new Unfreezer(this);
    }

    private sealed class Unfreezer(ControllableTimeProvider owner) : IDisposable
    {
        private int _disposed;

        // Idempotent: only the first dispose unfreezes, so a double-dispose of this token can't clobber
        // a later freeze. Combined with the single-freeze invariant, exactly one token owns each freeze.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref owner._frozenUtcTicks, NotFrozen);
        }
    }
}
