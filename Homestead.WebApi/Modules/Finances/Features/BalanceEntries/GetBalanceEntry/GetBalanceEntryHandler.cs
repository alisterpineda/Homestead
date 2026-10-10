using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;

public class GetBalanceEntryHandler(AppDbContext db)
{
    public async Task<BalanceEntryResponse> Handle(GetBalanceEntryQuery request, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, request.AccountId, ct);

        var entry = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.TransactionId, ct)
            ?? throw new EntityNotFoundException("Balance entry not found.");

        if (entry.AccountId != request.AccountId)
            throw new BusinessRuleException("Balance entry does not belong to this account.");

        return BalanceEntryMapping.ToResponse(entry);
    }
}
