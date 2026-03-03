using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.CreateGroup;

public class CreateGroupHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : ICommandHandler<CreateGroupRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateGroupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { Error = "Name is required." });

        var nameExists = await db.Groups
            .AnyAsync(g => g.Name == request.Name.Trim(), ct);

        if (nameExists)
            return Results.Conflict(new { Error = "A group with this name already exists." });

        var userId = currentUser.UserId;

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };

        db.Groups.Add(group);

        // Auto-add creator as Owner
        if (userId.HasValue)
        {
            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = userId.Value,
                AccessLevel = GroupAccessLevel.Owner
            });
        }

        await db.SaveChangesAsync(ct);

        var creatorName = userId.HasValue
            ? await db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return Results.Created($"/api/v1/admin/groups/{group.Id}", new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = group.CreatedAt,
            CreatedByUserId = group.CreatedByUserId,
            CreatedByUserName = creatorName,
            MemberCount = userId.HasValue ? 1 : 0
        });
    }
}
