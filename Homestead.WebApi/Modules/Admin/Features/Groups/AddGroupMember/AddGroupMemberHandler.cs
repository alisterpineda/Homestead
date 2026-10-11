using FluentValidation;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;

public class AddGroupMemberHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork, IValidator<AddGroupMemberRequest> validator)
{
    public async Task<GroupMemberResponse> HandleAsync(Guid id, AddGroupMemberRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var groupExists = await db.Groups.AnyAsync(g => g.Id == id, ct);
        if (!groupExists)
            throw new EntityNotFoundException("Group not found.");

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.IsActive, u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            throw new BusinessRuleException("User not found.");

        if (!user.IsActive)
            throw new BusinessRuleException("User is not active.");

        var alreadyMember = await db.GroupMembers
            .AnyAsync(gm => gm.GroupId == id && gm.UserId == request.UserId, ct);

        if (alreadyMember)
            throw new DuplicateEntityException("User is already a member of this group.");

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = id,
            UserId = request.UserId,
            AccessLevel = request.AccessLevel
        });

        audit.TrackScalar("Group", id, "MemberAdded",
            null, $"{user.DisplayName} as {request.AccessLevel}");

        await unitOfWork.SaveChangesAsync(ct);

        return new GroupMemberResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            AccessLevel = request.AccessLevel
        };
    }
}
