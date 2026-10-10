using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public record GetProcessingSchedulesQuery : PaginatedRequest
{
    public ProcessingJobType? JobType { get; init; }
    public bool? IsEnabled { get; init; }
}
