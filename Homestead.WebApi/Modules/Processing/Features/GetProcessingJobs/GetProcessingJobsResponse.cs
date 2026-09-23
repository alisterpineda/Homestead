using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Processing.Features.GetProcessingJobs;

public record GetProcessingJobsResponse
{
    public required Guid Id { get; init; }
    public required ProcessingJobType Type { get; init; }
    public required ProcessingJobStatus Status { get; init; }
    public ProcessingEntityType? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string? Error { get; init; }
    public int Priority { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public Guid? ParentJobId { get; init; }
    public int TotalChildCount { get; init; }
    public int CompletedChildCount { get; init; }
    public int FailedChildCount { get; init; }

    /// <summary>Runs that ended in an exception. Non-zero on a Pending row means it is waiting to retry.</summary>
    public int Attempts { get; init; }

    /// <summary>How many runs the job type allows. 1 for any type that has not opted into retries.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>When a scheduled retry becomes due; null when the job is claimable now.</summary>
    public DateTimeOffset? RunAfter { get; init; }
}
