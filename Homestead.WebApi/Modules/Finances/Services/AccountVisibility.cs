using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public static class AccountVisibility
{
    // User-facing reads must treat system (and missing) accounts as non-existent.
    public static async Task EnsureUserVisibleAsync(AppDbContext db, Guid accountId, CancellationToken ct)
    {
        var visible = await db.Accounts.AsNoTracking()
            .AnyAsync(a => a.Id == accountId && !a.IsSystem, ct);
        if (!visible)
            throw new EntityNotFoundException("Account not found."); // → 404 via DomainExceptionHandler
    }
}
