using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Processing;
using MediatR;

namespace Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

public record GetProcessingJobsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetProcessingJobsResponse>>
{
    public Guid? EntityId { get; init; }
    public ProcessingEntityType? EntityType { get; init; }
    public ProcessingJobStatus? Status { get; init; }
}
