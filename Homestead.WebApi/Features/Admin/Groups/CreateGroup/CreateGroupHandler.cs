using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.Groups.CreateGroup;

public class CreateGroupHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditContext audit)
    : IRequestHandler<CreateGroupCommand, GroupResponse>
{
    public async Task<GroupResponse> Handle(CreateGroupCommand request, CancellationToken ct)
    {
        var nameExists = await db.Groups
            .AnyAsync(g => g.Name == request.Name.Trim(), ct);

        if (nameExists)
            throw new DuplicateEntityException("A group with this name already exists.");

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
        audit.Created("Group", group.Id);
        await db.SaveChangesAsync(ct);

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

        var creatorName = userId.HasValue
            ? await db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = group.CreatedAt,
            CreatedByUserId = group.CreatedByUserId,
            CreatedByUserName = creatorName,
            MemberCount = userId.HasValue ? 1 : 0
        };
    }
}
