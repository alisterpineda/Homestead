using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public class UpdateGroupMemberHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateGroupMemberCommand, GroupMemberResponse>
{
    public async Task<GroupMemberResponse> Handle(UpdateGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            throw new EntityNotFoundException("Group member not found.");

        var oldAccessLevel = member.AccessLevel;

        if (oldAccessLevel == GroupAccessLevel.Owner
            && command.Request.AccessLevel != GroupAccessLevel.Owner)
        {
            var ownerCount = await db.GroupMembers
                .CountAsync(gm => gm.GroupId == command.GroupId
                                && gm.AccessLevel == GroupAccessLevel.Owner, ct);

            if (ownerCount <= 1)
                throw new BusinessRuleException(
                    "Cannot change the access level of the last owner of the group.");
        }

        member.AccessLevel = command.Request.AccessLevel;

        if (oldAccessLevel != command.Request.AccessLevel)
        {
            audit.TrackScalar("Group", command.GroupId, "MemberAccessLevel",
                $"{member.User.DisplayName} as {oldAccessLevel}",
                $"{member.User.DisplayName} as {command.Request.AccessLevel}");
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

public record UpdateGroupMemberCommand : ICommand<GroupMemberResponse>
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
    public required UpdateGroupMemberRequest Request { get; init; }
}
