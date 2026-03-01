using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public class CreateUserHandler(UserManager<AppUser> userManager)
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

        if (request.IsAdmin)
            await userManager.AddToRoleAsync(user, "Admin");

        return new UserResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            IsAdmin = request.IsAdmin,
            CreatedAt = user.CreatedAt
        };
    }
}
