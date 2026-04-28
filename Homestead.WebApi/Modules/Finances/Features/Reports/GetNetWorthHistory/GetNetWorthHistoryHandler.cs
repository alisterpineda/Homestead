using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;

public class GetNetWorthHistoryHandler(AppDbContext db, IValuationService valuations)
    : IRequestHandler<GetNetWorthHistoryQuery, GetNetWorthHistoryResponse>
{
    // Threshold below which "All" range stays daily; above, switches to monthly.
    private const int DailyAllRangeThresholdDays = 365;

    public async Task<GetNetWorthHistoryResponse> Handle(
        GetNetWorthHistoryQuery request, CancellationToken ct)
    {
        var to = request.AsOf ?? CurrentDayCutoffUtc();

        var settings = await FinanceSettings.LoadAsync(db, ct);
        if (settings.DisplayCurrencyId is not Guid displayCurrencyId)
        {
            return EmptyResponse(request.Range, to,
                displayCurrencyId: null,
                displayCurrencySymbol: null,
                granularity: NetWorthGranularity.Daily,
                reason: "no_display_currency");
        }

        var firstAt = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => !s.Account.IsSystem)
            .Select(s => (DateTimeOffset?)s.Transaction.OccurredAt)
            .MinAsync(ct);

        var symbol = await ResolveSymbolAsync(displayCurrencyId, ct);
        if (firstAt is null)
        {
            return EmptyResponse(request.Range, to,
                displayCurrencyId,
                symbol,
                NetWorthGranularity.Daily,
                reason: "no_transactions");
        }

        var (from, granularity) = ResolveRange(request.Range, to, firstAt.Value);
        var cutoffs = GenerateCutoffs(from, to, granularity);

        var ctx = await valuations.LoadContextAsync(to, ct);

        // Per SKILL.md and valuation.md: net worth = SUM(displayValue) over all
        // non-system accounts. Beancount sign convention makes this exact without
        // per-type arithmetic. Archived accounts are kept — they held value historically
        // and excluding them would dent the line on the archive date.
        var accountIds = await db.Accounts
            .AsNoTracking()
            .Where(a => !a.IsSystem)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var splits = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => accountIds.Contains(s.AccountId) && s.Transaction.OccurredAt <= to)
            .OrderBy(s => s.Transaction.OccurredAt)
            .Select(s => new SplitRow(
                s.Transaction.OccurredAt,
                s.AccountId,
                s.CommodityId,
                s.Quantity))
            .ToListAsync(ct);

        var running = new Dictionary<(Guid Account, Guid Commodity), decimal>();
        var points = new List<NetWorthHistoryPoint>(cutoffs.Count);
        var hasUnpriced = false;
        var splitIdx = 0;

        foreach (var cutoff in cutoffs)
        {
            while (splitIdx < splits.Count && splits[splitIdx].OccurredAt <= cutoff)
            {
                var s = splits[splitIdx++];
                var key = (s.AccountId, s.CommodityId);
                running[key] = running.GetValueOrDefault(key) + s.Quantity;
            }

            decimal pricedSum = 0m;
            var anyPriced = false;
            var anyUnpricedThisBucket = false;
            foreach (var ((_, commodityId), qty) in running)
            {
                if (qty == 0m) continue;
                var dv = ctx.ConvertToDisplayAsOf(commodityId, qty, cutoff);
                if (dv is { } v)
                {
                    pricedSum += v;
                    anyPriced = true;
                }
                else
                {
                    anyUnpricedThisBucket = true;
                }
            }

            var value = ValuationService.AggregateDisplayValue(pricedSum, anyPriced, anyUnpricedThisBucket);
            if (anyUnpricedThisBucket) hasUnpriced = true;
            points.Add(new NetWorthHistoryPoint(cutoff, value));
        }

        return new GetNetWorthHistoryResponse
        {
            DisplayCurrencyId = displayCurrencyId,
            DisplayCurrencySymbol = symbol,
            Granularity = granularity,
            Range = request.Range,
            From = from,
            To = to,
            Points = points,
            HasUnpriced = hasUnpriced,
            Reason = null,
        };
    }

    private async Task<string?> ResolveSymbolAsync(Guid commodityId, CancellationToken ct) =>
        await db.Commodities
            .AsNoTracking()
            .Where(c => c.Id == commodityId)
            .Select(c => c.Symbol)
            .FirstOrDefaultAsync(ct);

    private static GetNetWorthHistoryResponse EmptyResponse(
        NetWorthRange range,
        DateTimeOffset to,
        Guid? displayCurrencyId,
        string? displayCurrencySymbol,
        NetWorthGranularity granularity,
        string reason) => new()
        {
            DisplayCurrencyId = displayCurrencyId,
            DisplayCurrencySymbol = displayCurrencySymbol,
            Granularity = granularity,
            Range = range,
            From = null,
            To = to,
            Points = [],
            HasUnpriced = false,
            Reason = reason,
        };

    private static (DateTimeOffset From, NetWorthGranularity Granularity) ResolveRange(
        NetWorthRange range, DateTimeOffset to, DateTimeOffset earliestActivity)
    {
        DateTimeOffset Clamp(DateTimeOffset candidate) =>
            candidate < earliestActivity ? earliestActivity : candidate;

        return range switch
        {
            NetWorthRange.OneMonth => (Clamp(to.AddMonths(-1)), NetWorthGranularity.Daily),
            NetWorthRange.ThreeMonths => (Clamp(to.AddMonths(-3)), NetWorthGranularity.Daily),
            NetWorthRange.SixMonths => (Clamp(to.AddMonths(-6)), NetWorthGranularity.Daily),
            NetWorthRange.OneYear => (Clamp(to.AddYears(-1)), NetWorthGranularity.Daily),
            NetWorthRange.All => (
                earliestActivity,
                (to - earliestActivity).TotalDays > DailyAllRangeThresholdDays
                    ? NetWorthGranularity.Monthly
                    : NetWorthGranularity.Daily),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };
    }

    private static List<DateTimeOffset> GenerateCutoffs(
        DateTimeOffset from, DateTimeOffset to, NetWorthGranularity granularity)
    {
        var cutoffs = new List<DateTimeOffset>();
        if (granularity == NetWorthGranularity.Daily)
        {
            var fromDay = DateOnly.FromDateTime(from.UtcDateTime);
            var toDay = DateOnly.FromDateTime(to.UtcDateTime);
            for (var d = fromDay; d <= toDay; d = d.AddDays(1))
            {
                var cutoff = d.ToEndOfDayUtc();
                cutoffs.Add(cutoff > to ? to : cutoff);
            }
        }
        else
        {
            var current = new DateOnly(from.UtcDateTime.Year, from.UtcDateTime.Month, 1);
            var toMonth = new DateOnly(to.UtcDateTime.Year, to.UtcDateTime.Month, 1);
            while (current <= toMonth)
            {
                var lastDay = new DateOnly(
                    current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                var cutoff = lastDay.ToEndOfDayUtc();
                cutoffs.Add(cutoff > to ? to : cutoff);
                current = current.AddMonths(1);
            }
        }
        return cutoffs;
    }

    // Aligns with ValuationService.CurrentDayCutoffUtc — same-date balance entries
    // (stored at 23:59:59 UTC) are reflected on the day they assert.
    private static DateTimeOffset CurrentDayCutoffUtc() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).ToEndOfDayUtc();

    private readonly record struct SplitRow(
        DateTimeOffset OccurredAt, Guid AccountId, Guid CommodityId, decimal Quantity);
}
