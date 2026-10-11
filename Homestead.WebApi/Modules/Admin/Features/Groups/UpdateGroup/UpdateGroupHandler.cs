using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public class UpdateGroupHandler(
    AppDbContext db, IAuditContext audit, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork,
    IValidator<UpdateGroupRequest> validator)
{
    public async Task<GroupResponse> HandleAsync(Guid id, UpdateGroupRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            throw new EntityNotFoundException("Group not found.");

        var trimmedName = request.Name.Trim();

        var nameConflict = await db.Groups
            .AnyAsync(g => g.Id != id && g.Name == trimmedName, ct);

        if (nameConflict)
            throw new DuplicateEntityException("A group with this name already exists.");

        audit.Track<GroupSnapshot, Group>(group, "Group", group.Id);

        group.Name = trimmedName;
        group.Description = request.Description?.Trim();
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
