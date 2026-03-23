using Homestead.WebApi.Authentication;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetMyGroups;

public class GetMyGroupsHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMyGroupsQuery, IReadOnlyList<GetMyGroupsResponse>>
{
    public async Task<IReadOnlyList<GetMyGroupsResponse>> Handle(
        GetMyGroupsQuery request, CancellationToken ct)
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
