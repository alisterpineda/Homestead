using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public interface IPriceProvider
{
    CommodityPriceSource Source { get; }
    PriceProviderConfigSchema ConfigSchema { get; }
    IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; }
    IReadOnlyList<string> CoveredSecurityExchanges { get; }
    Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct);
}
