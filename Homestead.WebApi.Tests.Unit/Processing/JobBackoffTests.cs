using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobBackoffTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 60)]
    [InlineData(3, 300)]
    public void DefaultSchedule_IsIndexedByFailedAttempts(int failedAttempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), JobBackoff.ForAttempt(failedAttempts));
    }

    [Fact]
    public void PastTheEndOfTheSchedule_KeepsTheLastDelay()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), JobBackoff.ForAttempt(10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void BelowTheStartOfTheSchedule_ClampsToTheFirstDelay(int failedAttempts)
    {
        // Clamped rather than thrown: this runs on the failure path, where an exception would lose the real error.
        Assert.Equal(TimeSpan.FromSeconds(10), JobBackoff.ForAttempt(failedAttempts));
    }

    [Fact]
    public void AConfiguredSchedule_IsUsed()
    {
        TimeSpan[] schedule = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

        Assert.Equal(TimeSpan.FromSeconds(1), JobBackoff.ForAttempt(1, schedule));
        Assert.Equal(TimeSpan.FromSeconds(2), JobBackoff.ForAttempt(2, schedule));
        Assert.Equal(TimeSpan.FromSeconds(2), JobBackoff.ForAttempt(3, schedule));
    }

    [Fact]
    public void AnEmptySchedule_FallsBackToTheDefault()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), JobBackoff.ForAttempt(1, []));
    }

    [Fact]
    public void Options_ResolveRetryBackoff_FallsBackWhenUnset()
    {
        Assert.Same(JobBackoff.DefaultSchedule, new JobProcessingOptions().ResolveRetryBackoff());

        var configured = new JobProcessingOptions { RetryBackoff = [TimeSpan.FromSeconds(3)] };
        Assert.Equal([TimeSpan.FromSeconds(3)], configured.ResolveRetryBackoff());
    }
}
