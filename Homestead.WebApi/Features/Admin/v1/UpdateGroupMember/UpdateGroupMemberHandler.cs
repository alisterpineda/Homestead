using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;

public class UpdateGroupMemberHandler(AppDbContext db)
    : ICommandHandler<UpdateGroupMemberCommand, IResult>
{
    public async Task<IResult> HandleAsync(UpdateGroupMemberCommand command, CancellationToken ct)
    {
        var member = await db.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.UserId, ct);

        if (member is null)
            return Results.NotFound();

        member.AccessLevel = command.Request.AccessLevel;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new GroupMemberResponse
        {
            UserId = member.UserId,
            DisplayName = member.User.DisplayName,
            Email = member.User.Email,
            AccessLevel = member.AccessLevel
        });
    }
}

public record UpdateGroupMemberCommand
{
    public Guid GroupId { get; init; }
    public Guid UserId { get; init; }
    public required UpdateGroupMemberRequest Request { get; init; }
}
