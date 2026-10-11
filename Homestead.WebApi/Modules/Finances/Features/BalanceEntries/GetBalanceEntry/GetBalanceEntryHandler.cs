using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;

public class GetBalanceEntryHandler(AppDbContext db)
{
    public async Task<BalanceEntryResponse> HandleAsync(Guid accountId, Guid transactionId, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, accountId, ct);

        var entry = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Balance entry not found.");

        if (entry.AccountId != accountId)
            throw new BusinessRuleException("Balance entry does not belong to this account.");

        return BalanceEntryMapping.ToResponse(entry);
    }
}
