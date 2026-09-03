using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public interface IPriceProvider
{
    CommodityPriceSource Source { get; }

    // The "PriceSources:{Source}:Enabled" flag. Enablement alone admits the source to valuation's
    // active-source pool (its existing prices count even when the credentials have since broken).
    bool IsEnabled { get; }

    // Why the provider cannot import, independent of IsEnabled. Surfaced on the source's page and in
    // job errors so a broken source reads as "not configured, because <reason>".
    string? ConfigurationError { get; }

    // Enabled AND valid — what scheduled and Run-now imports require.
    bool IsConfigured { get; }

    // Read-only config surface for the source's page. Never the raw key: null when the provider takes
    // no key at all, else whether one is set.
    bool? HasApiKey { get; }
    string BaseUrl { get; }

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
