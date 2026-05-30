using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public class UpdateGroupHandler(
    AppDbContext db, IAuditContext audit, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateGroupCommand, GroupResponse>
{
    public async Task<GroupResponse> Handle(UpdateGroupCommand command, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == command.Id, ct);
        if (group is null)
            throw new EntityNotFoundException("Group not found.");

        var trimmedName = command.Request.Name.Trim();

        var nameConflict = await db.Groups
            .AnyAsync(g => g.Id != command.Id && g.Name == trimmedName, ct);

        if (nameConflict)
            throw new DuplicateEntityException("A group with this name already exists.");

        audit.Track<GroupSnapshot, Group>(group, "Group", group.Id);

        group.Name = trimmedName;
        group.Description = command.Request.Description?.Trim();
        group.UpdatedAt = DateTimeOffset.UtcNow;
        group.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(gm => gm.GroupId == group.Id, ct);
        var creatorName = group.CreatedByUserId.HasValue
            ? await db.Users.Where(u => u.Id == group.CreatedByUserId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        string? updatedByUserName = null;
        if (group.UpdatedByUserId.HasValue)
        {
            updatedByUserName = await db.Users
                .Where(u => u.Id == group.UpdatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = group.CreatedAt,
            UpdatedAt = group.UpdatedAt,
            UpdatedByUserId = group.UpdatedByUserId,
            UpdatedByUserName = updatedByUserName,
            CreatedByUserId = group.CreatedByUserId,
            CreatedByUserName = creatorName,
            MemberCount = memberCount
        };
    }
}

public record UpdateGroupCommand : ICommand<GroupResponse>
{
    public Guid Id { get; init; }
    public required UpdateGroupRequest Request { get; init; }
}
