using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateUser;

public class UpdateUserHandler(UserManager<AppUser> userManager, IRoleSourceManager roleSourceManager, AppDbContext db)
    : ICommandHandler<UpdateUserCommand, UserResponse?>
{
    public async Task<UserResponse?> HandleAsync(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.Id.ToString());
        if (user is null)
            return null;

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

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await userManager.ResetPasswordAsync(user, token, request.Password);
                if (!passwordResult.Succeeded)
                    throw new IdentityValidationException(passwordResult.ToErrorDictionary());
            }

            var newRole = request.Role.ToString();
            var currentRoles = await userManager.GetRolesAsync(user);
            if (!currentRoles.Contains(newRole))
            {
                var rolesToRemove = currentRoles.Intersect(AppRoles.All).ToList();
                if (rolesToRemove.Count > 0)
                    await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                await userManager.AddToRoleAsync(user, newRole);
                await roleSourceManager.SetSourceAsync(user.Id, newRole, RoleSource.Local);
            }

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
