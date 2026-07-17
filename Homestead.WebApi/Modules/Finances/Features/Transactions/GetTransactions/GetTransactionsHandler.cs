using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;

public class GetTransactionsHandler(AppDbContext db, ICategoryPathResolver pathResolver)
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
            var needle = LikeEscaper.Escape(request.MemoContains.Trim());
            query = query.Where(t => EF.Functions.ILike(t.Memo, $"%{needle}%", LikeEscaper.EscapeChar));
        }

        var orderedQuery = query
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id);

        var page = await orderedQuery.ToPaginatedAsync(request, ct);
        var categories = await pathResolver.GetPathMapAsync(ct);

        // Feeds DocumentLinkCount — same aux-map idiom as the category path map above.
        var linkCounts = await DocumentLinkCounts.ForPageAsync(
            db, page.Items.Select(t => t.Id).ToList(), ct);

        return new PaginatedResponse<TransactionResponse>
        {
            Items = page.Items.Select(t => TransactionMapping.ToResponse(t, categories, linkCounts)).ToList(),
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
        // user-facing shape of a normal transaction (Transfer / Buy / Sell / Dividend / Expense / Income).
        TransactionShape.Transfer =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Transfer),
        TransactionShape.InvestmentBuy =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.InvestmentBuy),
        TransactionShape.InvestmentSell =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.InvestmentSell),
        TransactionShape.Dividend =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Dividend),
        TransactionShape.Expense =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Expense),
        TransactionShape.Income =>
            query.OfType<NormalTransaction>().Where(t => t.NormalShape == NormalShape.Income),
        _ => query,
    };
}
