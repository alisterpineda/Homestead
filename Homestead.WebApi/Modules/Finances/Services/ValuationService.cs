using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class ValuationService(AppDbContext db) : IValuationService
{
    public async Task<ValuationContext> LoadContextAsync(CancellationToken ct)
    {
        var settings = await FinanceSettings.LoadAsync(db, ct);
        var displayCurrencyId = settings.DisplayCurrencyId;
        var cutoff = CurrentDayCutoffUtc();

        var prices = await db.CommodityPrices
            .AsNoTracking()
            .Where(p => p.PriceAt <= cutoff)
            .OrderBy(p => p.PriceAt)
            .ToListAsync(ct);

        var table = new Dictionary<(Guid, Guid), CommodityPrice>(prices.Count);
        var ids = new HashSet<Guid>();
        foreach (var p in prices)
        {
            // Last write wins; the asc-by-PriceAt order makes that the most recent.
            table[(p.CommodityId, p.QuoteCurrencyId)] = p;
            ids.Add(p.CommodityId);
            ids.Add(p.QuoteCurrencyId);
        }
        if (displayCurrencyId is { } d) ids.Add(d);

        return new ValuationContext(displayCurrencyId, table, ids);
    }

    public async Task<AccountValuation> ValueAccountAsync(
        Account account, ValuationContext ctx, CancellationToken ct)
    {
        var cutoff = CurrentDayCutoffUtc();
        var rows = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => s.AccountId == account.Id && s.Transaction.OccurredAt <= cutoff)
            .GroupBy(s => s.CommodityId)
            .Select(g => new
            {
                CommodityId = g.Key,
                Quantity = g.Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .ToListAsync(ct);

        var positions = rows
            .Select(r => new RawPosition(r.CommodityId, r.Quantity))
            .ToList();
        return BuildValuation(account, positions, ctx);
    }

    public async Task<IReadOnlyDictionary<Guid, AccountValuation>> ValueAccountsAsync(
        IReadOnlyCollection<Account> accounts, ValuationContext ctx, CancellationToken ct)
    {
        if (accounts.Count == 0)
            return new Dictionary<Guid, AccountValuation>();

        var ids = accounts.Select(a => a.Id).ToHashSet();
        var cutoff = CurrentDayCutoffUtc();

        var rows = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => ids.Contains(s.AccountId) && s.Transaction.OccurredAt <= cutoff)
            .GroupBy(s => new { s.AccountId, s.CommodityId })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.CommodityId,
                Quantity = g.Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .ToListAsync(ct);

        var byAccount = rows
            .GroupBy(r => r.AccountId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RawPosition>)g
                    .Select(r => new RawPosition(r.CommodityId, r.Quantity))
                    .ToList());

        var result = new Dictionary<Guid, AccountValuation>(accounts.Count);
        foreach (var a in accounts)
        {
            var positions = byAccount.GetValueOrDefault(a.Id, Array.Empty<RawPosition>());
            result[a.Id] = BuildValuation(a, positions, ctx);
        }
        return result;
    }

    private static AccountValuation BuildValuation(
        Account account, IReadOnlyList<RawPosition> rawPositions, ValuationContext ctx)
    {
        var nonZero = rawPositions.Where(p => p.Quantity != 0m).ToList();
        var positions = new List<PositionValuation>(nonZero.Count);
        decimal pricedSum = 0m;
        var anyPriced = false;
        var unpricedCount = 0;

        foreach (var raw in nonZero)
        {
            var displayValue = ctx.ConvertToDisplay(raw.CommodityId, raw.Quantity);
            var isPriced = displayValue is not null;
            positions.Add(new PositionValuation(raw.CommodityId, raw.Quantity, displayValue, isPriced));
            if (isPriced)
            {
                pricedSum += displayValue!.Value;
                anyPriced = true;
            }
            else
            {
                unpricedCount++;
            }
        }

        decimal? aggregateDisplay = (anyPriced, unpricedCount > 0) switch
        {
            (true, _) => pricedSum,
            (false, true) => null,
            _ => 0m,
        };

        var nativeBalance = account.Kind == AccountKind.Investment
            ? (decimal?)null
            : rawPositions
                .Where(p => p.CommodityId == account.ReportingCurrencyId)
                .Sum(p => p.Quantity);

        return new AccountValuation(aggregateDisplay, nativeBalance, unpricedCount, positions);
    }

    private readonly record struct RawPosition(Guid CommodityId, decimal Quantity);

    // Day-grain cutoff so same-date balance entries (stored at 23:59:59 UTC) are
    // reflected immediately. v1 valuation is "as of now" at day resolution; a future
    // ?asOf= parameter would replace this with the caller's instant.
    private static DateTimeOffset CurrentDayCutoffUtc() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).ToEndOfDayUtc();
}
