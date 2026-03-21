using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public class LoginHandler(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager)
    : ICommandHandler<LoginRequest, LoginResponse>
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return new LoginResponse(Succeeded: false, IsLockedOut: false, IsInactive: false);

        if (!user.IsActive)
            return new LoginResponse(Succeeded: false, IsLockedOut: false, IsInactive: true);

        // Use the TUser overload to avoid a second DB lookup inside SignInManager
        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);

        return new LoginResponse(result.Succeeded, result.IsLockedOut, IsInactive: false);
    }
}
