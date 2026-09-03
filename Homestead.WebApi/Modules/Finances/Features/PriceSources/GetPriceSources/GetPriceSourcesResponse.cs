using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public record PriceSourceSummary
{
    public required CommodityPriceSource Source { get; init; }
    public required string DisplayName { get; init; }
    public required CommodityPriceSourceKind Kind { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsConfigured { get; init; }
    public required int PriceCount { get; init; }
    public DateTimeOffset? LastImportAt { get; init; }
    public string? LastImportStatus { get; init; }
}

public record GetPriceSourcesResponse
{
    public required IReadOnlyList<PriceSourceSummary> Items { get; init; }
}
