using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;

public class GetBalanceEntriesHandler(AppDbContext db)
{
    public async Task<IReadOnlyList<BalanceEntryResponse>> HandleAsync(
        Guid accountId, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, accountId, ct);

        var entries = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return entries.Select(BalanceEntryMapping.ToResponse).ToList();
    }
}
