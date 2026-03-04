using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Persistence;

public interface IRoleSourceManager
{
    Task<RoleSource?> GetSourceAsync(Guid userId, string roleName);
    Task SetSourceAsync(Guid userId, string roleName, RoleSource source);
}
