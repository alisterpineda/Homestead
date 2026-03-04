using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

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
        await db.SaveChangesAsync();
    }

    private Task<AppUserRole?> FindUserRoleAsync(Guid userId, string roleName)
    {
        return (from ur in db.Set<AppUserRole>()
                join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == userId && r.Name == roleName
                select ur).FirstOrDefaultAsync();
    }
}
