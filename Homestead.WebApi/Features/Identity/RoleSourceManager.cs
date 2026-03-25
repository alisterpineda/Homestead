using Homestead.WebApi.Persistence;
using Homestead.WebApi.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Identity;

public class RoleSourceManager(AppDbContext db) : IRoleSourceManager
{
    public async Task<RoleSource?> GetSourceAsync(Guid userId, string roleName)
    {
        var userRole = await FindUserRoleAsync(userId, roleName);
        return userRole?.Source;
    }

    public async Task SetSourceAsync(Guid userId, string roleName, RoleSource source)
    {
        var userRole = await FindUserRoleAsync(userId, roleName);
        if (userRole is null) return;

        userRole.Source = source;
    }

    private Task<AppUserRole?> FindUserRoleAsync(Guid userId, string roleName)
    {
        return (from ur in db.Set<AppUserRole>()
                join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == userId && r.Name == roleName
                select ur).FirstOrDefaultAsync();
    }
}
