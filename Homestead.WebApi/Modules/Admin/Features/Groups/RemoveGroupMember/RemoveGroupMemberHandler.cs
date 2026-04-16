using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.RemoveGroupMember;

public class RemoveGroupMemberHandler(AppDbContext db, IAuditLogger auditLogger)
    : IRequestHandler<RemoveGroupMemberCommand, bool>
{
    public async Task<bool> Handle(RemoveGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            throw new EntityNotFoundException("Group member not found.");

        if (member.AccessLevel == GroupAccessLevel.Owner)
        {
            var ownerCount = await db.GroupMembers
                .CountAsync(gm => gm.GroupId == command.GroupId
                                && gm.AccessLevel == GroupAccessLevel.Owner, ct);

            if (ownerCount <= 1)
                throw new BusinessRuleException("Cannot remove the last owner of the group.");
        }

        auditLogger.LogUpdated("Group", command.GroupId,
        [
            new ScalarFieldChange("MemberRemoved", $"{member.User.DisplayName} as {member.AccessLevel}", null)
        ]);

        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        return true;
    }
}

public record RemoveGroupMemberCommand : IRequest<bool>
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
}
