using FluentValidation;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public class UpdateGroupMemberHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork, IValidator<UpdateGroupMemberRequest> validator)
{
    public async Task<GroupMemberResponse> HandleAsync(Guid id, Guid userId, UpdateGroupMemberRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == id && gm.UserId == userId, ct);

        if (member is null)
            throw new EntityNotFoundException("Group member not found.");

        var oldAccessLevel = member.AccessLevel;

        if (oldAccessLevel == GroupAccessLevel.Owner
            && request.AccessLevel != GroupAccessLevel.Owner)
        {
            var ownerCount = await db.GroupMembers
                .CountAsync(gm => gm.GroupId == id
                                && gm.AccessLevel == GroupAccessLevel.Owner, ct);

            if (ownerCount <= 1)
                throw new BusinessRuleException(
                    "Cannot change the access level of the last owner of the group.");
        }

        member.AccessLevel = request.AccessLevel;

        if (oldAccessLevel != request.AccessLevel)
        {
            audit.TrackScalar("Group", id, "MemberAccessLevel",
                $"{member.User.DisplayName} as {oldAccessLevel}",
                $"{member.User.DisplayName} as {request.AccessLevel}");
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new GroupMemberResponse
        {
            UserId = member.UserId,
            DisplayName = member.User.DisplayName,
            Email = member.User.Email,
            AccessLevel = member.AccessLevel
        };
    }
}
