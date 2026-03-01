using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Identity.v1.Register;

public class RegisterHandler(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager)
    : ICommandHandler<RegisterRequest, RegisterResponse>
{
    public async Task<RegisterResponse> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return new RegisterResponse
            {
                Succeeded = false,
                Errors = result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })
            };
        }

        if (await userManager.Users.CountAsync(ct) == 1)
            await userManager.AddToRoleAsync(user, "Admin");

        await signInManager.SignInAsync(user, isPersistent: true);
        return new RegisterResponse { Succeeded = true };
    }
}
