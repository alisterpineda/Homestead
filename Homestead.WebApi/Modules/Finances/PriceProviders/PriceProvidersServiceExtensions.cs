using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public static class PriceProvidersServiceExtensions
{
    public static IServiceCollection AddPriceProviders(this IServiceCollection services, IConfiguration _)
    {
        services.AddHttpClient<BankOfCanadaValetClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddKeyedScoped<IPriceProvider, BankOfCanadaPriceProvider>(CommodityPriceSource.BankOfCanada);

        services.AddHttpClient<AlphaVantageClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddKeyedScoped<IPriceProvider, AlphaVantagePriceProvider>(CommodityPriceSource.AlphaVantage);
        return services;
    }
}
