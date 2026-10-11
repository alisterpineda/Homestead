using Homestead.WebApi.Authentication;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public class GetMyGroupsHandler(AppDbContext db, ICurrentUserAccessor currentUser)
{
    public async Task<IReadOnlyList<GetMyGroupsResponse>> HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return [];

        return await db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == userId.Value)
            .Select(gm => new GetMyGroupsResponse
            {
                Id = gm.Group.Id,
                Name = gm.Group.Name,
                Description = gm.Group.Description,
                AccessLevel = gm.AccessLevel
            })
            .OrderBy(g => g.Name)
            .ToListAsync(ct);
    }
}
