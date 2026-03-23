using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.GetGroups;

public class GetGroupsHandler(AppDbContext db)
    : IRequestHandler<GetGroupsQuery, PaginatedResponse<GroupResponse>>
{
    public async Task<PaginatedResponse<GroupResponse>> Handle(
        GetGroupsQuery request, CancellationToken ct)
    {
        var query = db.Groups.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(search));
        }

        return await query
            .OrderBy(g => g.Name)
            .Select(g => new GroupResponse
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                CreatedAt = g.CreatedAt,
                CreatedByUserId = g.CreatedByUserId,
                CreatedByUserName = g.CreatedByUser != null ? g.CreatedByUser.DisplayName : null,
                MemberCount = g.Members.Count
            })
            .ToPaginatedAsync(request, ct);
    }
}
