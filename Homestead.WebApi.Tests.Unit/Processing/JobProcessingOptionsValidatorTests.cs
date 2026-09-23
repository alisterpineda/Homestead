using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobProcessingOptionsValidatorTests
{
    private readonly JobProcessingOptionsValidator _sut = new();

    [Fact]
    public void TheDefaults_AreValid()
    {
        // Load-bearing beyond tidiness: the build-time OpenAPI generator boots the host with no
        // configuration at all, and ValidateOnStart would fail that build.
        Assert.True(_sut.Validate(null, new JobProcessingOptions()).Succeeded);
    }

    [Fact]
    public void AHeartbeatNoFasterThanTheLease_IsRejected()
    {
        var options = new JobProcessingOptions
        {
            LeaseDuration = TimeSpan.FromMinutes(1),
            HeartbeatInterval = TimeSpan.FromMinutes(1),
        };

        var result = _sut.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("HeartbeatInterval", result.FailureMessage);
    }

    [Fact]
    public void NonPositiveIntervals_AreRejected()
    {
        var options = new JobProcessingOptions
        {
            LeaseDuration = TimeSpan.Zero,
            HeartbeatInterval = TimeSpan.FromSeconds(-1),
            PollInterval = TimeSpan.Zero,
        };

        var result = _sut.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LeaseDuration", result.FailureMessage);
        Assert.Contains("PollInterval", result.FailureMessage);
    }

    [Fact]
    public void ANegativeBackoff_IsRejected()
    {
        var options = new JobProcessingOptions { RetryBackoff = [TimeSpan.FromSeconds(-1)] };

        Assert.True(_sut.Validate(null, options).Failed);
    }
}
