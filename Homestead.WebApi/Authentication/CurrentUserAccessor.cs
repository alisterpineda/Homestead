using System.Security.Claims;
using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Authentication;

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public AppRole Role
    {
        get
        {
            var roleClaim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
            if (roleClaim is not null && Enum.TryParse<AppRole>(roleClaim, out var role))
                return role;
            return AppRole.Guest;
        }
    }
}
