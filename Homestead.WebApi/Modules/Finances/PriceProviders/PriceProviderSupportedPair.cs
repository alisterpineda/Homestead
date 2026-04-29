namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public record PriceProviderSupportedPair(string CommoditySymbol, string? Exchange, string QuoteCurrencySymbol);
