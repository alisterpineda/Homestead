using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances;

// Test doubles for the config-backed price-source registry. Enablement/configuration are static config
// in production, so tests toggle them synchronously here instead of seeding settings rows.
public static class TestPriceSources
{
    public static IPriceProvider Provider(
        CommodityPriceSource source,
        bool enabled = true,
        bool configured = true,
        string? error = null)
    {
        var provider = Substitute.For<IPriceProvider>();
        provider.Source.Returns(source);
        provider.IsEnabled.Returns(enabled);
        provider.IsConfigured.Returns(configured);
        provider.ConfigurationError.Returns(error);
        provider.HasApiKey.Returns((bool?)null);
        provider.BaseUrl.Returns("https://example.test/");
        provider.SupportedPairs.Returns(Array.Empty<PriceProviderSupportedPair>());
        provider.CoveredSecurityExchanges.Returns(Array.Empty<string>());
        provider.CoveredQuoteCurrencies.Returns(Array.Empty<string>());
        return provider;
    }
}

// Same rules as the production PriceSourceRegistry (answers come from the registered provider), over a
// dictionary instead of keyed DI — so a test that registers an enabled provider sees it as enabled
// everywhere, and one that registers nothing sees the source as unregistered/disabled.
public sealed class StubPriceSourceRegistry : IPriceSourceRegistry
{
    public Dictionary<CommodityPriceSource, IPriceProvider> Providers { get; } = [];

    public IPriceProvider? Get(CommodityPriceSource source) => Providers.GetValueOrDefault(source);

    public bool IsEnabled(CommodityPriceSource source) =>
        source.Kind() != CommodityPriceSourceKind.External || (Get(source)?.IsEnabled ?? false);

    public bool IsConfigured(CommodityPriceSource source) =>
        source.Kind() != CommodityPriceSourceKind.External || (Get(source)?.IsConfigured ?? false);

    public void Set(CommodityPriceSource source, bool enabled, bool configured) =>
        Providers[source] = TestPriceSources.Provider(source, enabled, configured);

    public IReadOnlySet<CommodityPriceSource> EnabledSources =>
        Enum.GetValues<CommodityPriceSource>().Where(IsEnabled).ToHashSet();
}
