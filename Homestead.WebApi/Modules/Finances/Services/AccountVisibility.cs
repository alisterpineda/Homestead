using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public static class AccountVisibility
{
    // User-facing reads must treat system, category, and missing accounts as non-existent — only
    // balance-sheet accounts (Asset/Liability) are "real" accounts the activity surface operates on.
    public static async Task EnsureUserVisibleAsync(AppDbContext db, Guid accountId, CancellationToken ct)
    {
        var visible = await db.Accounts.AsNoTracking()
            .Where(AccountQueries.IsBalanceSheetAccount)
            .AnyAsync(a => a.Id == accountId && !a.IsSystem, ct);
        if (!visible)
            throw new EntityNotFoundException("Account not found."); // → 404 via DomainExceptionHandler
    }
}
