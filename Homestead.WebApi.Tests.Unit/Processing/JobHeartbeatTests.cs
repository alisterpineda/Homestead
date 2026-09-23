using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Tests.Unit.Processing;

/// <summary>
/// When a run of failed heartbeats stops being "one missed beat" and becomes a lease that may already have
/// expired: giving up too early cancels long jobs on every DB blip, too late lets a reclaimed job run twice.
/// </summary>
public class JobHeartbeatTests
{
    private static readonly DateTimeOffset LastKnownExpiry = new(2026, 1, 1, 12, 0, 30, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    [Fact]
    public void AMissedBeat_WithTimeLeftOnTheLease_IsRidden()
    {
        // A beat after the first failure: 20s of lease left, two more beats' worth.
        var now = LastKnownExpiry - TimeSpan.FromSeconds(20);

        Assert.False(JobRunner.ShouldGiveUpLease(now, LastKnownExpiry, Interval));
    }

    [Fact]
    public void JustBeforeTheLastBeatThatCouldStillLand_KeepsGoing()
    {
        var now = LastKnownExpiry - Interval - TimeSpan.FromMilliseconds(1);

        Assert.False(JobRunner.ShouldGiveUpLease(now, LastKnownExpiry, Interval));
    }

    [Fact]
    public void OnceTheNextBeatWouldLandAfterExpiry_GivesUp()
    {
        var now = LastKnownExpiry - Interval;

        Assert.True(JobRunner.ShouldGiveUpLease(now, LastKnownExpiry, Interval));
    }

    [Fact]
    public void PastExpiry_GivesUp()
    {
        Assert.True(JobRunner.ShouldGiveUpLease(LastKnownExpiry + TimeSpan.FromSeconds(1), LastKnownExpiry, Interval));
    }
}
