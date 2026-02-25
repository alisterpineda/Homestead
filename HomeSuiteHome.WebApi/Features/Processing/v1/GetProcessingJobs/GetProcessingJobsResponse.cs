using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Features.Processing.v1.GetProcessingJobs;

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
}
