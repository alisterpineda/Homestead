using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Status.GetStatus;

public class GetStatusHandler(UserManager<AppUser> userManager)
    : IQueryHandler<GetStatusRequest, GetStatusResponse>
{
    public async Task<GetStatusResponse> HandleAsync(GetStatusRequest request, CancellationToken ct)
    {
        var hasUsers = await userManager.Users.AnyAsync(ct);
        return new GetStatusResponse { NeedsSetup = !hasUsers };
    }
}
