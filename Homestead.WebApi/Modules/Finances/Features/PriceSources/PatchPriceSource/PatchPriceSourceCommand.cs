using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public record PatchPriceSourceCommand : ICommand<PatchPriceSourceResponse>
{
    public CommodityPriceSource Source { get; init; }
    public bool? IsEnabled { get; init; }
    public string? Config { get; init; }
    public IReadOnlyList<string>? PairAllowList { get; init; }
}

public record PatchPriceSourceResponse(CommodityPriceSource Source, bool IsEnabled);
