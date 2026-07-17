using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;

public class GetAccountTransactionsHandler(AppDbContext db, ICategoryPathResolver pathResolver)
    : IRequestHandler<GetAccountTransactionsQuery, PaginatedResponse<TransactionResponse>>
{
    public async Task<PaginatedResponse<TransactionResponse>> Handle(
        GetAccountTransactionsQuery request, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, request.AccountId, ct);

        var query = db.AccountTransactions
            .AsNoTracking()
            .Include(t => t.Splits)
            .Where(t => t.Splits.Any(s => s.AccountId == request.AccountId));

        if (request.CommodityId is Guid commodityId)
            query = query.Where(t => t.Splits.Any(s => s.AccountId == request.AccountId
                                                    && s.CommodityId == commodityId));

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
}
