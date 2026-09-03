using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.PriceProviders;

// The production registry over real keyed DI registrations: this is the single definition of
// enabled/configured that valuation and the import paths share.
public class PriceSourceRegistryTests
{
    private static PriceSourceRegistry Registry(params IPriceProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
            services.AddKeyedSingleton(provider.Source, provider);
        return new PriceSourceRegistry(services.BuildServiceProvider());
    }

    [Fact]
    public void SystemKinds_AreAlwaysEnabledAndConfigured_WithNoProvider()
    {
        var registry = Registry();

        foreach (var source in new[] { CommodityPriceSource.Manual, CommodityPriceSource.Transaction })
        {
            Assert.Null(registry.Get(source));
            Assert.True(registry.IsEnabled(source));
            Assert.True(registry.IsConfigured(source));
        }
    }

    [Fact]
    public void UnregisteredExternal_IsDisabledAndNotConfigured()
    {
        var registry = Registry();

        Assert.Null(registry.Get(CommodityPriceSource.BankOfCanada));
        Assert.False(registry.IsEnabled(CommodityPriceSource.BankOfCanada));
        Assert.False(registry.IsConfigured(CommodityPriceSource.BankOfCanada));
    }

    [Fact]
    public void External_AnswersFromItsProvider()
    {
        var bankOfCanada = TestPriceSources.Provider(CommodityPriceSource.BankOfCanada, enabled: true, configured: true);
        var alphaVantage = TestPriceSources.Provider(
            CommodityPriceSource.AlphaVantage, enabled: true, configured: false, error: "ApiKey is required.");
        var registry = Registry(bankOfCanada, alphaVantage);

        Assert.Same(bankOfCanada, registry.Get(CommodityPriceSource.BankOfCanada));
        Assert.True(registry.IsEnabled(CommodityPriceSource.BankOfCanada));
        Assert.True(registry.IsConfigured(CommodityPriceSource.BankOfCanada));

        Assert.True(registry.IsEnabled(CommodityPriceSource.AlphaVantage));
        Assert.False(registry.IsConfigured(CommodityPriceSource.AlphaVantage));
    }

    // Valuation's pool is enablement alone: an enabled source whose credentials have broken still
    // contributes the prices it already imported. System kinds are always in.
    [Fact]
    public void EnabledSources_IsSystemKindsPlusEnabledExternals_IgnoringConfigured()
    {
        var registry = Registry(
            TestPriceSources.Provider(CommodityPriceSource.BankOfCanada, enabled: false, configured: false),
            TestPriceSources.Provider(CommodityPriceSource.AlphaVantage, enabled: true, configured: false));

        Assert.Equal(
            new HashSet<CommodityPriceSource>
            {
                CommodityPriceSource.Manual,
                CommodityPriceSource.Transaction,
                CommodityPriceSource.AlphaVantage,
            },
            registry.EnabledSources);
    }
}
