using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public class LoginHandler(SignInManager<AppUser> signInManager)
    : ICommandHandler<LoginRequest, LoginResponse>
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: true, lockoutOnFailure: true);

        return new LoginResponse(result.Succeeded, result.IsLockedOut);
    }
}
