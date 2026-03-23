using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroup;

public class UpdateGroupHandler(AppDbContext db, IAuditLogger auditLogger)
    : IRequestHandler<UpdateGroupCommand, GroupResponse>
{
    public async Task<GroupResponse> Handle(UpdateGroupCommand command, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == command.Id, ct);
        if (group is null)
            throw new NotFoundException("Group not found.");

        var trimmedName = command.Request.Name.Trim();

        var nameConflict = await db.Groups
            .AnyAsync(g => g.Id != command.Id && g.Name == trimmedName, ct);

        if (nameConflict)
            throw new ConflictException("A group with this name already exists.");

        var before = GroupSnapshot.From(group);

        group.Name = trimmedName;
        group.Description = command.Request.Description?.Trim();

        var after = GroupSnapshot.From(group);
        var changes = before.Diff(after);
        auditLogger.LogUpdated("Group", group.Id, changes);

        await db.SaveChangesAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(gm => gm.GroupId == group.Id, ct);
        var creatorName = group.CreatedByUserId.HasValue
            ? await db.Users.Where(u => u.Id == group.CreatedByUserId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = group.CreatedAt,
            CreatedByUserId = group.CreatedByUserId,
            CreatedByUserName = creatorName,
            MemberCount = memberCount
        };
    }
}

public record UpdateGroupCommand : IRequest<GroupResponse>
{
    public Guid Id { get; init; }
    public required UpdateGroupRequest Request { get; init; }
}
