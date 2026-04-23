namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices;

public record CommodityPriceResponse
{
    public required Guid Id { get; init; }
    public required Guid CommodityId { get; init; }
    public required Guid QuoteCurrencyId { get; init; }
    public required DateOnly PriceDate { get; init; }
    public required decimal Rate { get; init; }
}
