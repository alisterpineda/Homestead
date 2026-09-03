using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public static class PriceProvidersServiceExtensions
{
    public static IServiceCollection AddPriceProviders(this IServiceCollection services, IConfiguration configuration)
    {
        // Each options bag is bound once here and handed to the provider directly (rather than via
        // IOptions<T>) to match AddAiServices/AddTextExtraction and keep the provider ctors test-friendly.
        // An enabled-but-invalid bag still registers: the provider reports ConfigurationError instead of
        // the app refusing to boot.
        var bankOfCanada = PriceSourceConfiguration.Read<BankOfCanadaOptions>(configuration, CommodityPriceSource.BankOfCanada);
        services.AddHttpClient<BankOfCanadaValetClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddKeyedScoped<IPriceProvider>(CommodityPriceSource.BankOfCanada, (sp, _) =>
            new BankOfCanadaPriceProvider(
                sp.GetRequiredService<BankOfCanadaValetClient>(),
                bankOfCanada,
                sp.GetService<ILogger<BankOfCanadaPriceProvider>>()));

        var alphaVantage = PriceSourceConfiguration.Read<AlphaVantageOptions>(configuration, CommodityPriceSource.AlphaVantage);
        services.AddHttpClient<AlphaVantageClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddKeyedScoped<IPriceProvider>(CommodityPriceSource.AlphaVantage, (sp, _) =>
            new AlphaVantagePriceProvider(
                sp.GetRequiredService<AlphaVantageClient>(),
                alphaVantage,
                logger: sp.GetService<ILogger<AlphaVantagePriceProvider>>()));

        services.AddScoped<IPriceSourceRegistry, PriceSourceRegistry>();
        return services;
    }
}
