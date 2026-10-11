namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public record PatchPriceSourceBody
{
    public IReadOnlyList<string>? PairAllowList { get; init; }
}
