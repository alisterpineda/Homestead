using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public record GetProcessingSchedulesQuery : PaginatedRequest, IRequest<PaginatedResponse<GetProcessingSchedulesResponse>>
{
    public ProcessingJobType? JobType { get; init; }
    public bool? IsEnabled { get; init; }
}
