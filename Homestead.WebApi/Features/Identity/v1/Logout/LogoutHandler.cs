using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.Logout;

public class LogoutHandler(SignInManager<AppUser> signInManager)
    : ICommandHandler<LogoutRequest, LogoutResponse>
{
    public async Task<LogoutResponse> HandleAsync(LogoutRequest request, CancellationToken ct)
    {
        await signInManager.SignOutAsync();
        return new LogoutResponse();
    }
}
