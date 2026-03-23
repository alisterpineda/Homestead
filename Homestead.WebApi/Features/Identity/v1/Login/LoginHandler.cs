using MediatR;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public class LoginHandler(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
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
