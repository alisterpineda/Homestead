using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
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
        await AccountVisibility.EnsureUserVisibleAsync(db, request.AccountId, ct);

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
