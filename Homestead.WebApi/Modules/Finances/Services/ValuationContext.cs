using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public sealed class ValuationContext
{
    private readonly Dictionary<(Guid Commodity, Guid Quote), IReadOnlyList<CommodityPrice>> _prices;
    private readonly HashSet<Guid> _commodityIds;

    internal ValuationContext(
        Guid? displayCurrencyId,
        Dictionary<(Guid Commodity, Guid Quote), IReadOnlyList<CommodityPrice>> prices,
        HashSet<Guid> commodityIds)
    {
        DisplayCurrencyId = displayCurrencyId;
        _prices = prices;
        _commodityIds = commodityIds;
    }

    public Guid? DisplayCurrencyId { get; }

    public decimal? ConvertToDisplay(Guid commodityId, decimal quantity) =>
        ConvertToDisplayAsOf(commodityId, quantity, DateTimeOffset.MaxValue);

    public decimal? ConvertToDisplayAsOf(Guid commodityId, decimal quantity, DateTimeOffset asOf)
    {
        if (DisplayCurrencyId is not { } display) return null;
        var rate = ResolveRateAsOf(commodityId, display, asOf);
        return rate is null ? null : quantity * rate;
    }

    internal decimal? ResolveRate(Guid from, Guid to) =>
        ResolveRateAsOf(from, to, DateTimeOffset.MaxValue);

    internal decimal? ResolveRateAsOf(Guid from, Guid to, DateTimeOffset asOf)
    {
        if (from == to) return 1m;

        if (TryDirect(from, to, asOf, out var direct, out _))
            return direct;

        decimal? best = null;
        DateTimeOffset bestOlderLeg = default;
        foreach (var x in _commodityIds)
        {
            if (x == from || x == to) continue;
            if (!TryDirect(from, x, asOf, out var r1, out var t1)) continue;
            if (!TryDirect(x, to, asOf, out var r2, out var t2)) continue;
            var olderLeg = t1 < t2 ? t1 : t2;
            if (best is null || olderLeg > bestOlderLeg)
            {
                best = r1 * r2;
                bestOlderLeg = olderLeg;
            }
        }
        return best;
    }

    private bool TryDirect(Guid from, Guid to, DateTimeOffset asOf, out decimal rate, out DateTimeOffset priceAt)
    {
        if (FindAtOrBefore(from, to, asOf) is { } p && p.Rate != 0m)
        {
            rate = p.Rate;
            priceAt = p.PriceAt;
            return true;
        }
        if (FindAtOrBefore(to, from, asOf) is { } inv && inv.Rate != 0m)
        {
            rate = 1m / inv.Rate;
            priceAt = inv.PriceAt;
            return true;
        }
        rate = 0m;
        priceAt = default;
        return false;
    }

    private CommodityPrice? FindAtOrBefore(Guid commodity, Guid quote, DateTimeOffset asOf)
    {
        if (!_prices.TryGetValue((commodity, quote), out var list) || list.Count == 0)
            return null;

        // Binary search for the rightmost entry with PriceAt <= asOf.
        var lo = 0;
        var hi = list.Count - 1;
        var idx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (list[mid].PriceAt <= asOf)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return idx < 0 ? null : list[idx];
    }
}
