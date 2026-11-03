using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Processing.Hubs;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// What a test asks of the <see cref="TestNoOpJobHandler"/>. Every field defaults to "behave like the real
/// NoOp", so a job with no payload — the scheduler's, the smoke tests' — is unaffected.
/// </summary>
/// <param name="FailTimes">Throw on each run while the row's <c>Attempts</c> is below this.</param>
/// <param name="FailWith"><c>transient</c> (an HTTP 503), <c>permanent</c>, or <c>structured</c>.</param>
/// <param name="Block">Park the handler on <see cref="TestJobGate"/> until the test releases it.</param>
/// <param name="HookThrows">Make <c>OnFinallyFailedAsync</c> throw after recording itself.</param>
/// <param name="Timeout">What <c>ResolveTimeout</c> declares; null leaves the global budget.</param>
/// <param name="TimeoutThrows">Make <c>ResolveTimeout</c> throw instead of answering.</param>
public sealed record TestNoOpPayload(
    int FailTimes = 0,
    string FailWith = TestNoOpPayload.Transient,
    bool Block = false,
    bool HookThrows = false,
    TimeSpan? Timeout = null,
    bool TimeoutThrows = false)
{
    public const string Transient = "transient";
    public const string Permanent = "permanent";
    public const string Structured = "structured";
}

/// <summary>
/// Stands in for <see cref="NoOpJobHandler"/> in the integration host, so the processor's retry, lease, and
/// final-failure paths can be driven through a real job type without depending on any domain handler's
/// failure modes. Opts into retries — the production NoOp does not.
/// </summary>
public sealed class TestNoOpJobHandler(TestJobGate gate, JobHookProbe probe) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.NoOp;

    public int MaxAttempts => 3;

    public TimeSpan? ResolveTimeout(ProcessingJob job)
    {
        var payload = Read(job);
        if (payload.TimeoutThrows)
            throw new InvalidOperationException("ResolveTimeout threw on purpose.");
        return payload.Timeout;
    }

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = Read(job);

        if (payload.Block)
            await gate.EnterAsync(ct);

        if (job.Attempts < payload.FailTimes)
            throw payload.FailWith switch
            {
                TestNoOpPayload.Permanent => new InvalidOperationException($"Permanent failure on attempt {job.Attempts + 1}"),
                TestNoOpPayload.Structured => new TestStructuredJobException(job.Attempts + 1),
                _ => new HttpRequestException(
                    $"Transient failure on attempt {job.Attempts + 1}", null, HttpStatusCode.ServiceUnavailable),
            };
    }

    public Task OnFinallyFailedAsync(ProcessingJob job, Exception exception, CancellationToken ct)
    {
        probe.Record(job, exception);

        if (Read(job).HookThrows)
            throw new InvalidOperationException("The hook threw on purpose.");

        return Task.CompletedTask;
    }

    // Lenient: the real NoOp reads no payload, and schedules may carry one of their own.
    private static TestNoOpPayload Read(ProcessingJob job)
    {
        try
        {
            return job.GetPayload<TestNoOpPayload>() ?? new TestNoOpPayload();
        }
        catch (JsonException)
        {
            return new TestNoOpPayload();
        }
    }
}

public sealed class TestStructuredJobException(int attempt)
    : Exception($"Structured failure on attempt {attempt}"), IStructuredJobError
{
    public string ErrorCode => "test_structured";
    public Dictionary<string, string> ErrorData => [];
}

/// <summary>
/// Parks a <see cref="TestNoOpPayload.Block"/> job mid-handler so a test can act on its row while it runs.
/// Collection-wide singleton: call <see cref="Reset"/> at the start of any test that uses it.
/// </summary>
public sealed class TestJobGate
{
    private TaskCompletionSource _entered = NewSignal();
    private TaskCompletionSource _released = NewSignal();

    /// <summary>Completes once a blocked handler is inside the gate.</summary>
    public Task Entered => _entered.Task;

    public void Reset()
    {
        _released.TrySetResult();
        _entered = NewSignal();
        _released = NewSignal();
    }

    public void Release() => _released.TrySetResult();

    internal async Task EnterAsync(CancellationToken ct)
    {
        _entered.TrySetResult();
        await _released.Task.WaitAsync(ct);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Records every final-failure hook call the <see cref="TestNoOpJobHandler"/> receives.</summary>
public sealed class JobHookProbe
{
    private readonly ConcurrentQueue<(Guid JobId, ProcessingJobStatus Status, Exception Exception)> _calls = new();

    public void Record(ProcessingJob job, Exception exception) => _calls.Enqueue((job.Id, job.Status, exception));

    public IReadOnlyList<(Guid JobId, ProcessingJobStatus Status, Exception Exception)> CallsFor(Guid jobId) =>
        [.. _calls.Where(c => c.JobId == jobId)];
}

/// <summary>Records every job-status notification sent, so tests can assert what the frontend would hear.</summary>
public sealed class NotificationProbe
{
    private readonly ConcurrentQueue<(Guid JobId, ProcessingJobStatus Status)> _sent = new();

    public void Record(Guid jobId, ProcessingJobStatus status) => _sent.Enqueue((jobId, status));

    public IReadOnlyList<ProcessingJobStatus> StatusesFor(Guid jobId) =>
        [.. _sent.Where(n => n.JobId == jobId).Select(n => n.Status)];
}

/// <summary>Records each notification in <see cref="NotificationProbe"/>, then sends it as production would.</summary>
public sealed class RecordingProcessingNotifier(ProcessingNotifier inner, NotificationProbe probe) : IProcessingNotifier
{
    public Task NotifyJobStatusAsync(
        Guid jobId,
        ProcessingJobStatus status,
        ProcessingEntityType? entityType,
        Guid? entityId,
        string? documentName,
        string? error,
        Guid? userId,
        CancellationToken ct,
        string? errorCode = null,
        Dictionary<string, string>? errorData = null,
        int? totalChildCount = null,
        int? completedChildCount = null,
        int? failedChildCount = null)
    {
        probe.Record(jobId, status);
        return inner.NotifyJobStatusAsync(
            jobId, status, entityType, entityId, documentName, error, userId, ct,
            errorCode, errorData, totalChildCount, completedChildCount, failedChildCount);
    }
}
