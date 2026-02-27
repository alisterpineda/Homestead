using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Processing;

namespace Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

public record GetProcessingJobsRequest : PaginatedRequest
{
    public Guid? EntityId { get; init; }
    public ProcessingEntityType? EntityType { get; init; }
    public ProcessingJobStatus? Status { get; init; }
}
