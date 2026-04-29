namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public record PriceObservation(Guid CommodityId, Guid QuoteCurrencyId, DateTimeOffset PriceAt, decimal Rate);
