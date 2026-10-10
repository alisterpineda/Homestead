using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public record PatchPriceSourceCommand
{
    public CommodityPriceSource Source { get; init; }
    public IReadOnlyList<string>? PairAllowList { get; init; }
}

// PairAllowList is the normalised (trimmed + de-duplicated) list that was persisted; [] when the
// request sent none.
public record PatchPriceSourceResponse(CommodityPriceSource Source, IReadOnlyList<string> PairAllowList);
