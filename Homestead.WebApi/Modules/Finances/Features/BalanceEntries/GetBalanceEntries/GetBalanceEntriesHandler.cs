using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;

public class GetBalanceEntriesHandler(AppDbContext db)
    : IRequestHandler<GetBalanceEntriesQuery, IReadOnlyList<BalanceEntryResponse>>
{
    public async Task<IReadOnlyList<BalanceEntryResponse>> Handle(
        GetBalanceEntriesQuery request, CancellationToken ct)
    {
        var accountExists = await db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == request.AccountId, ct);

        if (!accountExists)
            throw new EntityNotFoundException("Account not found.");

        var entries = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .AsNoTracking()
            .Where(e => e.AccountId == request.AccountId)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return entries.Select(BalanceEntryMapping.ToResponse).ToList();
    }
}
