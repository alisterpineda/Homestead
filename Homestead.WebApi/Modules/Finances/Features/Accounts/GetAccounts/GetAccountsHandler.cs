using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public class GetAccountsHandler(AppDbContext db, IValuationService valuations)
{
    public async Task<PaginatedResponse<AccountResponse>> Handle(
        GetAccountsQuery request, CancellationToken ct)
    {
        // Balance-sheet accounts only — categories (non-system Income/Expense) are managed under
        // /categories and must not leak into the account list. !IsSystem stays for defence-in-depth.
        var query = db.Accounts.AsNoTracking()
            .Where(a => !a.IsSystem)
            .Where(AccountQueries.IsBalanceSheetAccount);

        if (request.IncludeArchived != true)
            query = query.Where(a => !a.IsArchived);

        if (request.Kind is { } kind)
            query = query.Where(a => a.Kind == kind);

        var ordered = query.OrderBy(a => a.Kind).ThenBy(a => a.Name);

        var totalCount = await ordered.CountAsync(ct);
        var pageItems = await ordered
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .ToListAsync(ct);

        var ctx = request.AsOf is { } asOf
            ? await valuations.LoadContextAsync(asOf, ct)
            : await valuations.LoadContextAsync(ct);
        var values = await valuations.ValueAccountsAsync(pageItems, ctx, ct);

        var pageIds = pageItems.Select(a => a.Id).ToHashSet();
        var txStatsByAccount = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => pageIds.Contains(s.AccountId))
            .GroupBy(s => s.AccountId)
            .Select(g => new { AccountId = g.Key, LatestDate = g.Max(s => s.Transaction.OccurredAt) })
            .ToDictionaryAsync(x => x.AccountId, x => (DateTimeOffset?)x.LatestDate, ct);

        var responses = pageItems
            .Select(a => AccountResponseMapper.ToResponse(
                a,
                values.GetValueOrDefault(a.Id),
                txStatsByAccount.ContainsKey(a.Id),
                txStatsByAccount.GetValueOrDefault(a.Id)))
            .ToList();

        return new PaginatedResponse<AccountResponse>
        {
            Items = responses,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = totalCount,
        };
    }
}
