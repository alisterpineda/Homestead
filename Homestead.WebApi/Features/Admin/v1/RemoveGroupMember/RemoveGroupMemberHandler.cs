using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.RemoveGroupMember;

public class RemoveGroupMemberHandler(AppDbContext db)
    : ICommandHandler<RemoveGroupMemberCommand, IResult>
{
    public async Task<IResult> HandleAsync(RemoveGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            return Results.NotFound();

        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}

public record RemoveGroupMemberCommand
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
}
