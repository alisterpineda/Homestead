using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.RemoveGroupMember;

public class RemoveGroupMemberHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> HandleAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == id && gm.UserId == userId, ct);

        if (member is null)
            throw new EntityNotFoundException("Group member not found.");

        if (member.AccessLevel == GroupAccessLevel.Owner)
        {
            var ownerCount = await db.GroupMembers
                .CountAsync(gm => gm.GroupId == id
                                && gm.AccessLevel == GroupAccessLevel.Owner, ct);

            if (ownerCount <= 1)
                throw new BusinessRuleException("Cannot remove the last owner of the group.");
        }

        audit.TrackScalar("Group", id, "MemberRemoved",
            $"{member.User.DisplayName} as {member.AccessLevel}", null);

        db.GroupMembers.Remove(member);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
