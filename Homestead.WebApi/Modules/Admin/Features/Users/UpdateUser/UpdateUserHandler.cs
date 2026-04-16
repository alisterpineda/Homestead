using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.UpdateUser;

public class UpdateUserHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditContext audit)
    : IRequestHandler<UpdateUserCommand, UserResponse?>
{
    public async Task<UserResponse?> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        if (currentUser.UserId is { } currentId && request.Id == currentId)
        {
            if (!request.IsActive)
                throw new BusinessRuleException("Cannot deactivate your own account.");
            if (request.Role != AppRole.Admin)
                throw new BusinessRuleException("Cannot demote your own admin role.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, ct);
        if (user is null)
            return null;

        var email = request.Email.Trim();
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)
            && await db.Users.AnyAsync(u => u.Id != user.Id && u.Email == email, ct))
        {
            throw new DuplicateEntityException("A user with that email already exists.");
        }

        var before = AppUserSnapshot.From(user, user.Role.ToString());

        user.Email = email;
        user.DisplayName = request.DisplayName.Trim();
        user.IsActive = request.IsActive;
        user.Role = request.Role;

        // Defer diff until after save so the snapshot reflects the actual committed state.
        audit.Track(() => before.Diff(AppUserSnapshot.From(user, user.Role.ToString())),
            "AppUser", user.Id);

        return new UserResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
        };
    }
}
