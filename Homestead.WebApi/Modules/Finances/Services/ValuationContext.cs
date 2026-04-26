using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public sealed class ValuationContext
{
    private readonly Dictionary<(Guid Commodity, Guid Quote), CommodityPrice> _prices;
    private readonly HashSet<Guid> _commodityIds;

    internal ValuationContext(
        Guid? displayCurrencyId,
        Dictionary<(Guid Commodity, Guid Quote), CommodityPrice> prices,
        HashSet<Guid> commodityIds)
    {
        DisplayCurrencyId = displayCurrencyId;
        _prices = prices;
        _commodityIds = commodityIds;
    }

    public Guid? DisplayCurrencyId { get; }

    public decimal? ConvertToDisplay(Guid commodityId, decimal quantity)
    {
        if (DisplayCurrencyId is not { } display) return null;
        var rate = ResolveRate(commodityId, display);
        return rate is null ? null : quantity * rate;
    }

    internal decimal? ResolveRate(Guid from, Guid to)
    {
        if (from == to) return 1m;

        if (TryDirect(from, to, out var direct, out _))
            return direct;

        decimal? best = null;
        DateTimeOffset bestOlderLeg = default;
        foreach (var x in _commodityIds)
        {
            if (x == from || x == to) continue;
            if (!TryDirect(from, x, out var r1, out var t1)) continue;
            if (!TryDirect(x, to, out var r2, out var t2)) continue;
            var olderLeg = t1 < t2 ? t1 : t2;
            if (best is null || olderLeg > bestOlderLeg)
            {
                best = r1 * r2;
                bestOlderLeg = olderLeg;
            }
        }
        return best;
    }

    private bool TryDirect(Guid from, Guid to, out decimal rate, out DateTimeOffset priceAt)
    {
        if (_prices.TryGetValue((from, to), out var p) && p.Rate != 0m)
        {
            rate = p.Rate;
            priceAt = p.PriceAt;
            return true;
        }
        if (_prices.TryGetValue((to, from), out var inv) && inv.Rate != 0m)
        {
            rate = 1m / inv.Rate;
            priceAt = inv.PriceAt;
            return true;
        }
        rate = 0m;
        priceAt = default;
        return false;
    }
}
