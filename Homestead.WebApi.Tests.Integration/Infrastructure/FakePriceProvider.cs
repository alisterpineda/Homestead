using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// A deterministic <see cref="IPriceProvider"/> that performs NO network IO. Registered keyed for
/// <see cref="CommodityPriceSource.BankOfCanada"/> in <see cref="HomesteadApiFactory"/> so the
/// import-pipeline tests exercise the real DB queries (commodity resolution + min-of-max watermark)
/// and the real unique-index enforcement without calling the live Bank of Canada Valet API.
///
/// <para>
/// <see cref="LastRequest"/> is captured on an INSTANCE field on purpose: a test that resolves this
/// provider from the SAME DI scope it invokes the import handler in can assert the resolved window
/// without racing background jobs — each scope gets its own scoped instance, so a job processed on
/// another scope (e.g. a RunNow enqueue from a different test) cannot overwrite it.
/// </para>
/// </summary>
public sealed class FakePriceProvider(BankOfCanadaOptions options) : IPriceProvider
{
    public CommodityPriceSource Source => CommodityPriceSource.BankOfCanada;

    // Enablement comes from the same "PriceSources:BankOfCanada:*" config the real provider reads
    // (HomesteadApiFactory pins Enabled=true), so the fake reports exactly what the readout endpoint
    // would show for a healthy real provider.
    public bool IsEnabled => options.Enabled;
    public string? ConfigurationError => null;
    public bool IsConfigured => IsEnabled;
    public bool? HasApiKey => null;
    public string BaseUrl => options.ResolveBaseUrl();

    // Kept non-empty so the provider-introspection endpoint test continues to see supported pairs, and so
    // held USD/EUR positions survive ImportCommodityPricesJobHandler.ResolveCommoditiesAsync's filter.
    public IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; } = new[]
    {
        new PriceProviderSupportedPair("USD", null, "CAD"),
        new PriceProviderSupportedPair("EUR", null, "CAD"),
    };

    public IReadOnlyList<string> CoveredSecurityExchanges => Array.Empty<string>();

    public IReadOnlyList<string> CoveredQuoteCurrencies => Array.Empty<string>();

    // Mirror the real Bank of Canada provider so watermark-anchored windows are predictable.
    public TimeSpan RevisionLookback => TimeSpan.FromDays(5);

    /// <summary>The request handed to the most recent <see cref="ImportAsync"/> call on this instance.</summary>
    public PriceImportRequest? LastRequest { get; private set; }

    public Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(PriceImportResult.Success(Array.Empty<PriceObservation>()));
    }
}
