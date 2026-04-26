using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public class GetPositionsHandler(AppDbContext db, IValuationService valuations)
    : IRequestHandler<GetPositionsQuery, IReadOnlyList<PositionResponse>>
{
    public async Task<IReadOnlyList<PositionResponse>> Handle(
        GetPositionsQuery request, CancellationToken ct)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);

        if (account is null || account.Kind != AccountKind.Investment)
            throw new EntityNotFoundException("Account not found.");

        var ctx = await valuations.LoadContextAsync(ct);
        var valuation = await valuations.ValueAccountAsync(account, ctx, ct);

        if (valuation.Positions.Count == 0)
            return Array.Empty<PositionResponse>();

        var commodityIds = valuation.Positions.Select(p => p.CommodityId).ToHashSet();
        var symbols = await db.Commodities
            .AsNoTracking()
            .Where(c => commodityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Symbol, ct);

        var pricedTotal = valuation.Positions
            .Where(p => p.IsPriced)
            .Sum(p => p.DisplayValue!.Value);

        var rows = valuation.Positions.Select(p => new
        {
            Position = p,
            Symbol = symbols.GetValueOrDefault(p.CommodityId, string.Empty),
            ShareOfAccount = p.IsPriced && pricedTotal != 0m
                ? (decimal?)(p.DisplayValue!.Value / pricedTotal)
                : null,
        });

        return rows
            .OrderByDescending(r => r.Position.IsPriced)
            .ThenByDescending(r => r.Position.DisplayValue ?? 0m)
            .ThenBy(r => r.Symbol, StringComparer.Ordinal)
            .Select(r => new PositionResponse
            {
                CommodityId = r.Position.CommodityId,
                Quantity = r.Position.Quantity,
                DisplayValue = r.Position.DisplayValue,
                ShareOfAccount = r.ShareOfAccount,
            })
            .ToList();
    }
}
