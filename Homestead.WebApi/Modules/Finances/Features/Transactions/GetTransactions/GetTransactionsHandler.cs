using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;

public class GetTransactionsHandler(AppDbContext db)
    : IRequestHandler<GetTransactionsQuery, PaginatedResponse<TransactionResponse>>
{
    public async Task<PaginatedResponse<TransactionResponse>> Handle(
        GetTransactionsQuery request, CancellationToken ct)
    {
        var query = db.AccountTransactions
            .AsNoTracking()
            .Include(t => t.Splits)
            .AsQueryable();

        if (request.AccountId is Guid accountId)
        {
            await AccountVisibility.EnsureUserVisibleAsync(db, accountId, ct);
            query = query.Where(t => t.Splits.Any(s => s.AccountId == accountId));
        }

        if (request.CommodityId is Guid commodityId)
            query = query.Where(t => t.Splits.Any(s => s.CommodityId == commodityId
                                                    || s.UnitPriceCommodityId == commodityId));

        if (request.DateFrom is DateOnly from)
        {
            var fromUtc = from.ToStartOfDayUtc();
            query = query.Where(t => t.OccurredAt >= fromUtc);
        }

        if (request.DateTo is DateOnly to)
        {
            var toUtcExclusive = to.AddDays(1).ToStartOfDayUtc();
            query = query.Where(t => t.OccurredAt < toUtcExclusive);
        }

        if (request.Shape is TransactionShape shape)
            query = ApplyShapeFilter(query, shape);

        if (!string.IsNullOrWhiteSpace(request.MemoContains))
        {
            var needle = request.MemoContains.Trim();
            query = query.Where(t => EF.Functions.ILike(t.Memo, $"%{needle}%"));
        }

        var orderedQuery = query
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id);

        var page = await orderedQuery.ToPaginatedAsync(request, ct);

        return new PaginatedResponse<TransactionResponse>
        {
            Items = page.Items.Select(TransactionMapping.ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
        };
    }

    private static IQueryable<AccountTransaction> ApplyShapeFilter(
        IQueryable<AccountTransaction> query, TransactionShape shape) => shape switch
    {
        TransactionShape.BalanceEntry => query.OfType<BalanceEntryTransaction>(),
        TransactionShape.OpeningBalance => query.OfType<OpeningBalanceTransaction>(),
        // Mirrors TransactionMapping.ResolveShape: the NormalShape discriminator distinguishes the
        // user-facing shape of a normal transaction (Transfer / Buy / Sell / Dividend).
        TransactionShape.Transfer =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Transfer),
        TransactionShape.InvestmentBuy =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.InvestmentBuy),
        TransactionShape.InvestmentSell =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.InvestmentSell),
        TransactionShape.Dividend =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Dividend),
        _ => query,
    };
}
