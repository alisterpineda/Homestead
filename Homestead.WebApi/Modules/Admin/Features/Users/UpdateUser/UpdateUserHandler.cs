using Homestead.WebApi.Modules.Identity;
using Homestead.WebApi.Modules.Identity.Services;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using MediatR;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.UpdateUser;

public class UpdateUserHandler(UserManager<AppUser> userManager, IRoleSourceManager roleSourceManager, AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
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

        var user = await userManager.FindByIdAsync(request.Id.ToString());
        if (user is null)
            return null;

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = AppRoles.Resolve(currentRoles).ToString();
        var before = AppUserSnapshot.From(user, currentRole);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            user.Email = request.Email;
            user.UserName = request.Email;
            user.DisplayName = request.DisplayName.Trim();
            user.IsActive = request.IsActive;

            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new IdentityValidationException(result.ToErrorDictionary());

            var newRole = request.Role.ToString();
            if (!currentRoles.Contains(newRole))
            {
                var rolesToRemove = currentRoles.Intersect(AppRoles.All).ToList();
                if (rolesToRemove.Count > 0)
                    await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                await userManager.AddToRoleAsync(user, newRole);
                await roleSourceManager.SetSourceAsync(user.Id, newRole, RoleSource.Local);
            }

            var after = AppUserSnapshot.From(user, request.Role.ToString());
            var changes = before.Diff(after);
            auditLogger.LogUpdated("AppUser", user.Id, changes);
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return new UserResponse
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                Email = user.Email,
                Role = request.Role,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };
        });
    }

}
