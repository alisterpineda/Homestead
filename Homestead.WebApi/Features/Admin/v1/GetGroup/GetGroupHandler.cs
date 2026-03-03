using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.GetGroup;

public class GetGroupHandler(AppDbContext db)
    : IQueryHandler<GetGroupRequest, GroupDetailResponse?>
{
    public async Task<GroupDetailResponse?> HandleAsync(GetGroupRequest request, CancellationToken ct)
    {
        return await db.Groups
            .AsNoTracking()
            .Where(g => g.Id == request.Id)
            .Select(g => new GroupDetailResponse
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                CreatedAt = g.CreatedAt,
                CreatedByUserId = g.CreatedByUserId,
                CreatedByUserName = g.CreatedByUser != null ? g.CreatedByUser.DisplayName : null,
                Members = g.Members.Select(m => new GroupMemberResponse
                {
                    UserId = m.UserId,
                    DisplayName = m.User.DisplayName,
                    Email = m.User.Email,
                    AccessLevel = m.AccessLevel
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
