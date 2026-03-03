using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.RemoveGroupMember;

public class RemoveGroupMemberHandler(AppDbContext db)
    : ICommandHandler<RemoveGroupMemberCommand, bool>
{
    public async Task<bool> HandleAsync(RemoveGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            throw new NotFoundException("Group member not found.");

        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        return true;
    }
}

public record RemoveGroupMemberCommand
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
}
