using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;

public record PriceSourceScheduleSummary
{
    public required Guid Id { get; init; }
    public required string CronExpression { get; init; }
    public required bool IsEnabled { get; init; }
    public required int Priority { get; init; }
    public required DateTimeOffset NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
}

public record GetPriceSourceResponse
{
    public required CommodityPriceSource Source { get; init; }
    public required string DisplayName { get; init; }
    public required CommodityPriceSourceKind Kind { get; init; }
    public required bool IsEnabled { get; init; }
    public string? Config { get; init; }
    public IReadOnlyList<string>? PairAllowList { get; init; }
    public DateTimeOffset? LastImportAt { get; init; }
    public string? LastImportStatus { get; init; }
    public string? LastImportMessage { get; init; }
    public required PriceProviderConfigSchema ConfigSchema { get; init; }
    public required IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; init; }
    public required IReadOnlyList<string> CoveredSecurityExchanges { get; init; }
    public required IReadOnlyList<string> CoveredQuoteCurrencies { get; init; }
    public PriceSourceScheduleSummary? Schedule { get; init; }
    public required int PriceCount { get; init; }
}
