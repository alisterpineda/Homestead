using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.UpdateUser;

public class UpdateUserHandler(UserManager<AppUser> userManager, AppDbContext db)
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

            if (request.IsAdmin)
                await userManager.AddToRoleAsync(user, "Admin");
            else
                await userManager.RemoveFromRoleAsync(user, "Admin");

            await transaction.CommitAsync(ct);

            return new UserResponse
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                Email = user.Email,
                IsAdmin = request.IsAdmin,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };
        });
    }
}
