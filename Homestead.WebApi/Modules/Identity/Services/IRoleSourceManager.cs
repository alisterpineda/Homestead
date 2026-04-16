using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Modules.Identity.Services;

public interface IRoleSourceManager
{
    Task<RoleSource?> GetSourceAsync(Guid userId, string roleName);
    Task SetSourceAsync(Guid userId, string roleName, RoleSource source);
}
