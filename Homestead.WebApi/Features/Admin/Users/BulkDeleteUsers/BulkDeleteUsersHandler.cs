using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.Users.BulkDeleteUsers;

public class BulkDeleteUsersHandler(
    UserManager<AppUser> userManager,
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<BulkDeleteUsersCommand, BulkDeleteUsersResponse>
{
    public async Task<BulkDeleteUsersResponse> Handle(BulkDeleteUsersCommand request, CancellationToken ct)
    {
        if (currentUser.UserId is { } currentId && request.Ids.Contains(currentId))
        {
            throw new BusinessRuleException("Cannot delete your own account.");
        }

        var users = await db.Users
            .Where(u => request.Ids.Contains(u.Id))
            .ToListAsync(ct);

        if (users.Count == 0)
            return new BulkDeleteUsersResponse(0);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            foreach (var user in users)
            {
                auditLogger.LogDeleted("AppUser", user.Id, description: user.DisplayName);

                var result = await userManager.DeleteAsync(user);
                if (!result.Succeeded)
                    throw new IdentityValidationException(result.ToErrorDictionary());
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new BulkDeleteUsersResponse(users.Count);
        });
    }
}
