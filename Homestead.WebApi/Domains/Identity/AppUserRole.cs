using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Domains.Identity;

public class AppUserRole : IdentityUserRole<Guid>
{
    public RoleSource Source { get; set; } = RoleSource.Local;
}
