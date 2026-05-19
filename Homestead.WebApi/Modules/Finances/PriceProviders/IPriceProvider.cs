using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public interface IPriceProvider
{
    CommodityPriceSource Source { get; }
    PriceProviderConfigSchema ConfigSchema { get; }
    IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; }
    IReadOnlyList<string> CoveredSecurityExchanges { get; }
    IReadOnlyList<string> CoveredQuoteCurrencies { get; }

    /// <summary>
    /// How far back beyond a known watermark this provider should re-fetch to
    /// catch retroactive revisions from the upstream source. Applied only on
    /// Watermark-anchored runs; ExplicitOverride runs use the caller's FromAt verbatim.
    /// </summary>
    TimeSpan RevisionLookback { get; }

    Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct);
}
