using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

// The single definition of "enabled" / "configured" for a price source. External sources answer from
// their provider's static config ("PriceSources:{Source}:*"); Manual and Transaction are always-on by
// convention. Replaces the old DB-backed IsEnabled lookup.
public interface IPriceSourceRegistry
{
    // Null for System kinds and for External sources with no registered provider.
    IPriceProvider? Get(CommodityPriceSource source);

    bool IsEnabled(CommodityPriceSource source);

    bool IsConfigured(CommodityPriceSource source);

    // Valuation's active-source pool: System kinds plus every External source whose flag is on.
    IReadOnlySet<CommodityPriceSource> EnabledSources { get; }
}
