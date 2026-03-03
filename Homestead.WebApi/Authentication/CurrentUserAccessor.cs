using System.Security.Claims;
using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Authentication;

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public Guid? UserId => ParseUserId(httpContextAccessor.HttpContext?.User);

    public AppRole Role => ParseRole(httpContextAccessor.HttpContext?.User);

    public static Guid? ParseUserId(ClaimsPrincipal? user)
    {
        var value = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static AppRole ParseRole(ClaimsPrincipal? user)
    {
        var roleClaim = user?.FindFirstValue(ClaimTypes.Role);
        if (roleClaim is not null && Enum.TryParse<AppRole>(roleClaim, out var role))
            return role;
        return AppRole.Guest;
    }
}
