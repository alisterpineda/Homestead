using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public class CreateUserHandler(UserManager<AppUser> userManager, IRoleSourceManager roleSourceManager, IAuditContext audit)
    : IRequestHandler<CreateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(CreateUserCommand request, CancellationToken ct)
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

        audit.Created("AppUser", user.Id);

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
