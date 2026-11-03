using System.Collections.Concurrent;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Integration.Processing;

/// <summary>
/// A handler may declare its own budget (<see cref="IJobHandler.ResolveTimeout"/>) in place of
/// <c>Processing:JobTimeout</c> — what the OCR handlers do with an engine's <c>JobTimeout</c>. Driven through
/// the test NoOp handler, whose payload says what to declare; each test builds a runner with its own
/// global budget so the two can be told apart.
/// </summary>
[Collection("Integration")]
public class JobTimeoutTests : JobRunnerTestBase
{
    public JobTimeoutTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AHandlerDeclaredBudget_OverridesAShorterGlobalOne()
    {
        await using var _ = await PauseAsync();
        var runner = RunnerWith(TimeSpan.FromMilliseconds(300));
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true, Timeout: TimeSpan.FromSeconds(10))));

        var run = runner.RunOnceAsync(CT);
        await Gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), CT);

        // Twice the global budget: under it, the job would already have been cancelled.
        await Task.Delay(TimeSpan.FromMilliseconds(600), CT);
        Gate.Release();

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(10), CT));
        Assert.Equal(ProcessingJobStatus.Completed, (await ReadAsync(jobId)).Status);
    }

    [Fact]
    public async Task AHandlerDeclaredBudget_ShorterThanTheGlobalOne_TimesTheJobOut()
    {
        await using var _ = await PauseAsync();
        var runner = RunnerWith(TimeSpan.FromSeconds(10));
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(
            Block: true, Timeout: TimeSpan.FromMilliseconds(300))));

        Assert.True(await runner.RunOnceAsync(CT).WaitAsync(TimeSpan.FromSeconds(5), CT));

        var failed = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("timed out after 00:00:00.3", failed.Error);

        var timeout = Assert.IsType<JobTimeoutException>(Assert.Single(Probe.CallsFor(jobId)).Exception);
        Assert.Equal(TimeSpan.FromMilliseconds(300), timeout.Timeout);
    }

    [Fact]
    public async Task AZeroDeclaredBudget_FallsBackToTheGlobalOne()
    {
        await using var _ = await PauseAsync();
        var runner = RunnerWith(TimeSpan.FromMilliseconds(300));
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true, Timeout: TimeSpan.Zero)));

        Assert.True(await runner.RunOnceAsync(CT).WaitAsync(TimeSpan.FromSeconds(10), CT));

        Assert.Equal(ProcessingJobStatus.Failed, (await ReadAsync(jobId)).Status);
        var timeout = Assert.IsType<JobTimeoutException>(Assert.Single(Probe.CallsFor(jobId)).Exception);
        Assert.Equal(TimeSpan.FromMilliseconds(300), timeout.Timeout);
    }

    // Asking how long to wait must not be able to fail the job: the global budget applies, with a warning.
    [Fact]
    public async Task AThrowingResolveTimeout_FallsBackToTheGlobalOne_AndWarns()
    {
        await using var _ = await PauseAsync();
        var logger = new RecordingLogger<JobRunner>();
        var runner = RunnerWith(TimeSpan.FromMilliseconds(300), logger);
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true, TimeoutThrows: true)));

        Assert.True(await runner.RunOnceAsync(CT).WaitAsync(TimeSpan.FromSeconds(10), CT));

        Assert.Equal(ProcessingJobStatus.Failed, (await ReadAsync(jobId)).Status);
        var timeout = Assert.IsType<JobTimeoutException>(Assert.Single(Probe.CallsFor(jobId)).Exception);
        Assert.Equal(TimeSpan.FromMilliseconds(300), timeout.Timeout);

        var warning = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Could not resolve the timeout"));
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    private JobRunner RunnerWith(TimeSpan globalTimeout, ILogger<JobRunner>? logger = null)
    {
        var configured = Factory.Services.GetRequiredService<IOptions<JobProcessingOptions>>().Value;
        return new JobRunner(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobProcessingOptions
            {
                JobTimeout = globalTimeout,
                LeaseDuration = configured.LeaseDuration,
                HeartbeatInterval = configured.HeartbeatInterval,
                PollInterval = configured.PollInterval,
                RetryBackoff = configured.RetryBackoff,
            }),
            logger ?? Factory.Services.GetRequiredService<ILogger<JobRunner>>());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
