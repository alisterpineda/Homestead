using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.Groups.AddGroupMember;

public class AddGroupMemberHandler(AppDbContext db, IAuditLogger auditLogger)
    : IRequestHandler<AddGroupMemberCommand, GroupMemberResponse>
{
    public async Task<GroupMemberResponse> Handle(AddGroupMemberCommand command, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == command.GroupId, ct);
        if (!groupExists)
            throw new EntityNotFoundException("Group not found.");

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == command.Request.UserId)
            .Select(u => new { u.Id, u.IsActive, u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            throw new BusinessRuleException("User not found.");

        if (!user.IsActive)
            throw new BusinessRuleException("User is not active.");

        var alreadyMember = await db.GroupMembers
            .AnyAsync(gm => gm.GroupId == command.GroupId && gm.UserId == command.Request.UserId, ct);

        if (alreadyMember)
            throw new DuplicateEntityException("User is already a member of this group.");

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = command.GroupId,
            UserId = command.Request.UserId,
            AccessLevel = command.Request.AccessLevel
        });

        auditLogger.LogUpdated("Group", command.GroupId,
        [
            new ScalarFieldChange("MemberAdded", null, $"{user.DisplayName} as {command.Request.AccessLevel}")
        ]);

        await db.SaveChangesAsync(ct);

        return new GroupMemberResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            AccessLevel = command.Request.AccessLevel
        };
    }
}

public record AddGroupMemberCommand : IRequest<GroupMemberResponse>
{
    public Guid GroupId { get; init; }
    public required AddGroupMemberRequest Request { get; init; }
}
