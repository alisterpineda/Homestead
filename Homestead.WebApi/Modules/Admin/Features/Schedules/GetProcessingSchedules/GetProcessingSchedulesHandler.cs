using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public class GetProcessingSchedulesHandler(AppDbContext db)
    : IRequestHandler<GetProcessingSchedulesQuery, PaginatedResponse<GetProcessingSchedulesResponse>>
{
    public async Task<PaginatedResponse<GetProcessingSchedulesResponse>> Handle(
        GetProcessingSchedulesQuery request, CancellationToken ct)
    {
        var query = db.ProcessingSchedules.AsNoTracking();

        if (request.JobType is not null)
            query = query.Where(s => s.JobType == request.JobType);

        if (request.IsEnabled is not null)
            query = query.Where(s => s.IsEnabled == request.IsEnabled);

        return await query
            .OrderBy(s => s.Name)
            .Select(s => new GetProcessingSchedulesResponse
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                JobType = s.JobType,
                Priority = s.Priority,
                CronExpression = s.CronExpression,
                TimeZone = s.TimeZone,
                IsEnabled = s.IsEnabled,
                CreatedByUserId = s.CreatedByUserId,
                CreatedAt = s.CreatedAt,
                LastRunAt = s.LastRunAt,
                NextRunAt = s.NextRunAt,
                LastJobId = s.LastJobId,
                LastRunStatus = db.ProcessingJobs
                    .Where(j => j.Id == s.LastJobId)
                    .Select(j => (Domain.Processing.ProcessingJobStatus?)j.Status)
                    .FirstOrDefault(),
            })
            .ToPaginatedAsync(request, ct);
    }
}
