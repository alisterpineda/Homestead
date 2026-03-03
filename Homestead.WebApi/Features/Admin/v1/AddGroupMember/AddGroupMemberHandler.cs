using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.AddGroupMember;

public class AddGroupMemberHandler(AppDbContext db)
    : ICommandHandler<AddGroupMemberCommand, IResult>
{
    public async Task<IResult> HandleAsync(AddGroupMemberCommand command, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == command.GroupId, ct);
        if (!groupExists)
            return Results.NotFound();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == command.Request.UserId)
            .Select(u => new { u.Id, u.IsActive, u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return Results.BadRequest(new { Error = "User not found." });

        if (!user.IsActive)
            return Results.BadRequest(new { Error = "User is not active." });

        var alreadyMember = await db.GroupMembers
            .AnyAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.Request.UserId, ct);

        if (alreadyMember)
            return Results.Conflict(new { Error = "User is already a member of this group." });

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = command.GroupId,
            UserId = command.Request.UserId,
            AccessLevel = command.Request.AccessLevel
        });

        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/admin/groups/{command.GroupId}/members/{user.Id}", new GroupMemberResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            AccessLevel = command.Request.AccessLevel
        });
    }
}

public record AddGroupMemberCommand
{
    public Guid GroupId { get; init; }
    public required AddGroupMemberRequest Request { get; init; }
}
