using MediatR;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.Logout;

public class LogoutHandler(SignInManager<AppUser> signInManager)
    : IRequestHandler<LogoutRequest, LogoutResponse>
{
    public async Task<LogoutResponse> Handle(LogoutRequest request, CancellationToken ct)
    {
        await signInManager.SignOutAsync();
        return new LogoutResponse();
    }
}
