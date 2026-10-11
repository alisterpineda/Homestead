using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;

public class BulkDeleteUsersHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit)
{
    public async Task<BulkDeleteUsersResponse> HandleAsync(BulkDeleteUsersCommand request, CancellationToken ct)
    {
        if (currentUser.UserId is { } currentId && request.Ids.Contains(currentId))
            throw new BusinessRuleException("Cannot delete your own account.");

        var users = await db.Users
            .Where(u => request.Ids.Contains(u.Id))
            .ToListAsync(ct);

        if (users.Count == 0)
            return new BulkDeleteUsersResponse(0);

        foreach (var user in users)
        {
            audit.Deleted("AppUser", user.Id, description: user.DisplayName);
            db.Users.Remove(user);
        }

        return new BulkDeleteUsersResponse(users.Count);
    }
}
