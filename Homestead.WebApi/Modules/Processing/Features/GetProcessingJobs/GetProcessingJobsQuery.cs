using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using MediatR;

namespace Homestead.WebApi.Modules.Processing.Features.GetProcessingJobs;

public record GetProcessingJobsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetProcessingJobsResponse>>
{
    public Guid? EntityId { get; init; }
    public ProcessingEntityType? EntityType { get; init; }
    public ProcessingJobStatus? Status { get; init; }
    public Guid? ParentJobId { get; init; }
}
