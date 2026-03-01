using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;

public class BulkDeleteUsersHandler(
    UserManager<AppUser> userManager,
    AppDbContext db,
    ICurrentUserAccessor currentUser)
    : ICommandHandler<BulkDeleteUsersRequest, BulkDeleteUsersResponse>
{
    public async Task<BulkDeleteUsersResponse> HandleAsync(BulkDeleteUsersRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is { } currentId && request.Ids.Contains(currentId))
        {
            throw new InvalidOperationException("Cannot delete your own account.");
        }

        var users = await db.Users
            .Where(u => request.Ids.Contains(u.Id))
            .ToListAsync(ct);

        if (users.Count == 0)
            return new BulkDeleteUsersResponse(0);

        foreach (var user in users)
            await userManager.DeleteAsync(user);

        return new BulkDeleteUsersResponse(users.Count);
    }
}
