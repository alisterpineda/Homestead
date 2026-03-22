using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public class CreateUserHandler(UserManager<AppUser> userManager, IRoleSourceManager roleSourceManager, IAuditLogger auditLogger, AppDbContext db)
    : ICommandHandler<CreateUserRequest, UserResponse>
{
    public async Task<UserResponse> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new IdentityValidationException(result.ToErrorDictionary());

        var roleName = request.Role.ToString();
        await userManager.AddToRoleAsync(user, roleName);
        await roleSourceManager.SetSourceAsync(user.Id, roleName, RoleSource.Local);

        auditLogger.LogCreated("AppUser", user.Id);
        await db.SaveChangesAsync(ct);

        return new UserResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = request.Role,
            IsActive = user.IsActive,
            HasPassword = true,
            CreatedAt = user.CreatedAt
        };
    }
}
