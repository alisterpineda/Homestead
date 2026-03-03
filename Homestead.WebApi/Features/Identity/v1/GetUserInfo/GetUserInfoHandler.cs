using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Identity.v1.GetUserInfo;

public class GetUserInfoHandler(UserManager<AppUser> userManager)
    : IQueryHandler<GetUserInfoRequest, GetUserInfoResponse?>
{
    public async Task<GetUserInfoResponse?> HandleAsync(GetUserInfoRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);

        return new GetUserInfoResponse
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Role = AppRoles.Resolve(roles),
            IsActive = user.IsActive
        };
    }
}
