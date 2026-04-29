using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public interface IPriceProvider
{
    CommodityPriceSource Source { get; }
    PriceProviderConfigSchema ConfigSchema { get; }
    IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; }
    Task<IReadOnlyList<PriceObservation>> ImportAsync(PriceImportRequest request, CancellationToken ct);
}
