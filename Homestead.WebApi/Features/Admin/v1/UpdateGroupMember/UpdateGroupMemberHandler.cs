using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;

public class UpdateGroupMemberHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<UpdateGroupMemberCommand, GroupMemberResponse>
{
    public async Task<GroupMemberResponse> HandleAsync(UpdateGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            throw new NotFoundException("Group member not found.");

        var oldAccessLevel = member.AccessLevel;
        member.AccessLevel = command.Request.AccessLevel;

        if (oldAccessLevel != command.Request.AccessLevel)
        {
            auditLogger.LogUpdated("Group", command.GroupId, currentUser.UserId,
            [
                new ScalarFieldChange("MemberAccessLevel",
                    $"{member.User.DisplayName} as {oldAccessLevel}",
                    $"{member.User.DisplayName} as {command.Request.AccessLevel}")
            ]);
        }

        await db.SaveChangesAsync(ct);

        return new GroupMemberResponse
        {
            UserId = member.UserId,
            DisplayName = member.User.DisplayName,
            Email = member.User.Email,
            AccessLevel = member.AccessLevel
        };
    }
}

public record UpdateGroupMemberCommand
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
    public required UpdateGroupMemberRequest Request { get; init; }
}
