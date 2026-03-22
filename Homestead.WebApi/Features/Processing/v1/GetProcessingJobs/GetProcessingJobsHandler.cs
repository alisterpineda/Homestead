using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

public class GetProcessingJobsHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetProcessingJobsRequest, PaginatedResponse<GetProcessingJobsResponse>>
{
    public async Task<PaginatedResponse<GetProcessingJobsResponse>> Handle(
        GetProcessingJobsRequest request, CancellationToken ct)
    {
        var query = db.ProcessingJobs.AsNoTracking();

        if (currentUser.Role != AppRole.Admin)
            query = query.Where(j => j.CreatedByUserId == currentUser.UserId);

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
                CreatedByUserId = j.CreatedByUserId
            })
            .ToPaginatedAsync(request, ct);
    }
}
