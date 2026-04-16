using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Processing.Features.GetProcessingJobs;

public class GetProcessingJobsHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetProcessingJobsQuery, PaginatedResponse<GetProcessingJobsResponse>>
{
    public async Task<PaginatedResponse<GetProcessingJobsResponse>> Handle(
        GetProcessingJobsQuery request, CancellationToken ct)
    {
        var query = db.ProcessingJobs.AsNoTracking();

        if (currentUser.Role != AppRole.Admin)
            query = query.Where(j => j.CreatedByUserId == currentUser.UserId);

        if (request.ParentJobId is not null)
            query = query.Where(j => j.ParentJobId == request.ParentJobId);
        else
            query = query.Where(j => j.ParentJobId == null);

        if (request.EntityId is not null)
            query = query.Where(j => j.EntityId == request.EntityId);

        if (request.EntityType is not null)
            query = query.Where(j => j.EntityType == request.EntityType);

        if (request.Status is not null)
            query = query.Where(j => j.Status == request.Status);

        return await query
            .OrderByDescending(j => j.CreatedAt)
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
                CreatedByUserId = j.CreatedByUserId,
                ParentJobId = j.ParentJobId,
                TotalChildCount = j.TotalChildCount,
                CompletedChildCount = j.CompletedChildCount,
                FailedChildCount = j.FailedChildCount
            })
            .ToPaginatedAsync(request, ct);
    }
}
