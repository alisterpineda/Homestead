using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

// PairAllowList is the normalised (trimmed + de-duplicated) list that was persisted; [] when the
// request sent none.
public record PatchPriceSourceResponse(CommodityPriceSource Source, IReadOnlyList<string> PairAllowList);
