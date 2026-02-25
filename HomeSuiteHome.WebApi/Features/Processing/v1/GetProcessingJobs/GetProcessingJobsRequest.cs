using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Features.Processing.v1.GetProcessingJobs;

public record GetProcessingJobsRequest : PaginatedRequest
{
    public Guid? EntityId { get; init; }
    public ProcessingEntityType? EntityType { get; init; }
    public ProcessingJobStatus? Status { get; init; }
}
