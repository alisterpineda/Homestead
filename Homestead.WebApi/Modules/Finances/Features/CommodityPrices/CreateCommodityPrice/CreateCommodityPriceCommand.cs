namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;

public record CreateCommodityPriceCommand
{
    public required Guid CommodityId { get; init; }
    public required Guid QuoteCurrencyId { get; init; }
    public required DateTimeOffset PriceAt { get; init; }
    public required decimal Rate { get; init; }
}
