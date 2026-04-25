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
            query = query.Where(t => t.Splits.Any(s => s.AccountId == accountId));

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

        if (!string.IsNullOrWhiteSpace(request.DescriptionContains))
        {
            var needle = request.DescriptionContains.Trim();
            query = query.Where(t => EF.Functions.ILike(t.Description, $"%{needle}%"));
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
        IQueryable<AccountTransaction> query, TransactionShape shape)
    {
        // Use the literal description here rather than the IsOpeningBalance helper because EF
        // can translate a direct equality comparison but not the helper method.
        var openingBalance = OpeningBalanceTransactions.Description;
        return shape switch
        {
            TransactionShape.BalanceEntry => query.OfType<BalanceEntryTransaction>(),
            TransactionShape.StockSplit => query.OfType<StockSplitTransaction>(),
            TransactionShape.OpeningBalance => query
                .OfType<NormalTransaction>()
                .Where(t => t.Description == openingBalance),
            TransactionShape.Transfer => query
                .OfType<NormalTransaction>()
                .Where(t => t.Description != openingBalance),
            _ => query,
        };
    }
}
