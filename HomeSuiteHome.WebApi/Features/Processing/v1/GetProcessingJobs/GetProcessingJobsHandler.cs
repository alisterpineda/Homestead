using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Processing.v1.GetProcessingJobs;

public class GetProcessingJobsHandler(AppDbContext db)
    : IQueryHandler<GetProcessingJobsRequest, PaginatedResponse<GetProcessingJobsResponse>>
{
    public async Task<PaginatedResponse<GetProcessingJobsResponse>> HandleAsync(
        GetProcessingJobsRequest request, CancellationToken ct)
    {
        var query = db.ProcessingJobs.AsNoTracking();

        if (request.EntityId is not null)
            query = query.Where(j => j.EntityId == request.EntityId);

        if (request.EntityType is not null)
            query = query.Where(j => j.EntityType == request.EntityType);

        if (request.Status is not null)
            query = query.Where(j => j.Status == request.Status);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(j => new GetProcessingJobsResponse
            {
                Id = j.Id,
                Type = j.Type,
                Status = j.Status,
                EntityType = j.EntityType,
                EntityId = j.EntityId,
                Error = j.Error,
                Priority = j.Priority,
                CreatedAt = j.CreatedAt,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt,
                CreatedByUserId = j.CreatedByUserId
            })
            .ToListAsync(ct);

        return new PaginatedResponse<GetProcessingJobsResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
