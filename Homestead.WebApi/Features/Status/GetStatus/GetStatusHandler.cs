using Homestead.WebApi.Authentication;
using MediatR;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Features.Status.GetStatus;

public class GetStatusHandler(UserManager<AppUser> userManager, IOptions<OidcOptions> oidcOptions)
    : IRequestHandler<GetStatusRequest, GetStatusResponse>
{
    public async Task<GetStatusResponse> Handle(GetStatusRequest request, CancellationToken ct)
    {
        var hasUsers = await userManager.Users.AnyAsync(ct);
        var oidc = oidcOptions.Value;

        return new GetStatusResponse
        {
            NeedsSetup = !hasUsers,
            Oidc = oidc.IsConfigured
                ? new OidcStatusResponse
                {
                    DisplayName = oidc.DisplayName,
                    DisableLocalLogin = oidc.DisableLocalLogin
                }
                : null
        };
    }
}
