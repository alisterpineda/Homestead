using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Features.Processing.GetProcessingJobs;

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
}
