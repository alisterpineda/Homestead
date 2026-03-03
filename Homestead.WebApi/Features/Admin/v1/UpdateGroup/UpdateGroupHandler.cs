using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroup;

public class UpdateGroupHandler(AppDbContext db)
    : ICommandHandler<UpdateGroupCommand, IResult>
{
    public async Task<IResult> HandleAsync(UpdateGroupCommand command, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == command.Id, ct);
        if (group is null)
            return Results.NotFound();

        var trimmedName = command.Request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return Results.BadRequest(new { Error = "Name is required." });

        var nameConflict = await db.Groups
            .AnyAsync(g => g.Id != command.Id && g.Name == trimmedName, ct);

        if (nameConflict)
            return Results.Conflict(new { Error = "A group with this name already exists." });

        group.Name = trimmedName;
        group.Description = command.Request.Description?.Trim();

        await db.SaveChangesAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(gm => gm.GroupId == group.Id, ct);
        var creatorName = group.CreatedByUserId.HasValue
            ? await db.Users.Where(u => u.Id == group.CreatedByUserId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return Results.Ok(new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = group.CreatedAt,
            CreatedByUserId = group.CreatedByUserId,
            CreatedByUserName = creatorName,
            MemberCount = memberCount
        });
    }
}

public record UpdateGroupCommand
{
    public Guid Id { get; init; }
    public required UpdateGroupRequest Request { get; init; }
}
