using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
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
        ValidateCustomRange(request);

        var to = request.AsOf?.ToUniversalTime() ?? CurrentDayCutoffUtc();

        var settings = await FinanceSettings.LoadAsync(db, ct);
        if (settings.DisplayCurrencyId is not Guid displayCurrencyId)
        {
            return EmptyResponse(request.Range, to,
                displayCurrencyId: null,
                displayCurrencySymbol: null,
                granularity: NetWorthGranularity.Daily,
                reason: "no_display_currency");
        }

        // Balance-sheet accounts only. Category (Income/Expense) splits must not seed the earliest-
        // activity date — and (below) must not fold into net worth. IsBalanceSheetAccount inlined here
        // because it targets the split's navigation, which the stored Expression can't be applied to.
        var firstAt = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => s.Account.AccountType == AccountType.Asset
                     || s.Account.AccountType == AccountType.Liability)
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

        var (from, resolvedTo, granularity) = ResolveRange(
            request.Range, request.FromDate, request.ToDate, to, firstAt.Value);
        // Preset month windows extend to end-of-current-month and Custom accepts any
        // ToDate; clamp so we never emit future-dated points.
        to = resolvedTo > to ? to : resolvedTo;
        var cutoffs = GenerateCutoffs(from, to, granularity);

        var ctx = await valuations.LoadContextAsync(to, ct);

        // Per SKILL.md and valuation.md: net worth = SUM(displayValue) over all
        // non-system accounts. Beancount sign convention makes this exact without
        // per-type arithmetic. Archived accounts are kept — they held value historically
        // and excluding them would dent the line on the archive date.
        // Balance-sheet accounts only. This both stops category balances folding into net worth and
        // keeps Kind!.Value below safe — categories have Kind == null and would throw here.
        var accountInfos = await db.Accounts
            .AsNoTracking()
            .Where(a => !a.IsSystem)
            .Where(AccountQueries.IsBalanceSheetAccount)
            .Select(a => new { a.Id, a.Kind })
            .ToListAsync(ct);

        var accountIds = accountInfos.Select(a => a.Id).ToList();
        var accountTypes = accountInfos.ToDictionary(
            a => a.Id,
            a => a.Kind!.Value.ToAccountType());

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

            DisplayBucket total = default, assets = default, liabilities = default;
            foreach (var ((accountId, commodityId), qty) in running)
            {
                if (qty == 0m) continue;
                var dv = ctx.ConvertToDisplayAsOf(commodityId, qty, cutoff);
                ref var side = ref accountTypes[accountId] == AccountType.Liability
                    ? ref liabilities
                    : ref assets;
                total.Add(dv);
                side.Add(dv);
            }

            if (total.AnyUnpriced) hasUnpriced = true;
            points.Add(new NetWorthHistoryPoint(
                cutoff, total.Aggregate(), assets.Aggregate(), liabilities.Aggregate()));
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

    private static (DateTimeOffset From, DateTimeOffset To, NetWorthGranularity Granularity) ResolveRange(
        NetWorthRange range, DateOnly? customFrom, DateOnly? customTo,
        DateTimeOffset to, DateTimeOffset earliestActivity)
    {
        DateTimeOffset Clamp(DateTimeOffset candidate) =>
            candidate < earliestActivity ? earliestActivity : candidate;

        NetWorthGranularity GranularityForSpan(DateTimeOffset from, DateTimeOffset upper) =>
            (upper - from).TotalDays > DailyAllRangeThresholdDays
                ? NetWorthGranularity.Monthly
                : NetWorthGranularity.Daily;

        switch (range)
        {
            case NetWorthRange.ThisMonth:
            {
                var firstOfMonth = new DateOnly(to.UtcDateTime.Year, to.UtcDateTime.Month, 1)
                    .ToStartOfDayUtc();
                return (Clamp(firstOfMonth), to, NetWorthGranularity.Daily);
            }
            case NetWorthRange.ThreeMonths:
            {
                var (from, end) = PriorCalendarMonthsWindow(to, 3);
                return (Clamp(from), end, GranularityForSpan(from, end));
            }
            case NetWorthRange.SixMonths:
            {
                var (from, end) = PriorCalendarMonthsWindow(to, 6);
                return (Clamp(from), end, GranularityForSpan(from, end));
            }
            case NetWorthRange.TwelveMonths:
            {
                var (from, end) = PriorCalendarMonthsWindow(to, 12);
                return (Clamp(from), end, GranularityForSpan(from, end));
            }
            case NetWorthRange.YearToDate:
            {
                var jan1 = new DateOnly(to.UtcDateTime.Year, 1, 1).ToStartOfDayUtc();
                return (Clamp(jan1), to, NetWorthGranularity.Daily);
            }
            case NetWorthRange.LastYear:
            {
                var year = to.UtcDateTime.Year - 1;
                var from = new DateOnly(year, 1, 1).ToStartOfDayUtc();
                var lastYearTo = new DateOnly(year, 12, 31).ToEndOfDayUtc();
                return (Clamp(from), lastYearTo, GranularityForSpan(from, lastYearTo));
            }
            case NetWorthRange.All:
                return (earliestActivity, to, GranularityForSpan(earliestActivity, to));
            case NetWorthRange.Custom:
            {
                var from = customFrom!.Value.ToStartOfDayUtc();
                var upper = customTo!.Value.ToEndOfDayUtc();
                return (Clamp(from), upper, GranularityForSpan(from, upper));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(range), range, null);
        }
    }

    // Calendar-aligned window of the last N calendar months including the current
    // month. Today's day-of-month is irrelevant — only which month `to` falls in.
    private static (DateTimeOffset From, DateTimeOffset To) PriorCalendarMonthsWindow(
        DateTimeOffset to, int months)
    {
        var firstOfCurrentMonth = new DateOnly(to.UtcDateTime.Year, to.UtcDateTime.Month, 1);
        var startMonth = firstOfCurrentMonth.AddMonths(-(months - 1));
        var endDay = new DateOnly(firstOfCurrentMonth.Year, firstOfCurrentMonth.Month,
            DateTime.DaysInMonth(firstOfCurrentMonth.Year, firstOfCurrentMonth.Month));
        return (startMonth.ToStartOfDayUtc(), endDay.ToEndOfDayUtc());
    }

    private static void ValidateCustomRange(GetNetWorthHistoryQuery request)
    {
        if (request.Range != NetWorthRange.Custom) return;
        if (request.FromDate is null || request.ToDate is null)
        {
            throw new BusinessRuleException(
                "FromDate and ToDate are required when Range is Custom.");
        }
        if (request.FromDate.Value > request.ToDate.Value)
        {
            throw new BusinessRuleException(
                "FromDate must be on or before ToDate.");
        }
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

    private struct DisplayBucket
    {
        public decimal PricedSum;
        public bool AnyPriced;
        public bool AnyUnpriced;

        public void Add(decimal? displayValue)
        {
            if (displayValue is { } v)
            {
                PricedSum += v;
                AnyPriced = true;
            }
            else
            {
                AnyUnpriced = true;
            }
        }

        public readonly decimal? Aggregate() =>
            ValuationService.AggregateDisplayValue(PricedSum, AnyPriced, AnyUnpriced);
    }
}
