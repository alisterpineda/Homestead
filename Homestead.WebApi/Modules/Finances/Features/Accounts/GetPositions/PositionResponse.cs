namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public record PositionResponse
{
    public required Guid CommodityId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? DisplayValue { get; init; }
    public decimal? ShareOfAccount { get; init; }
}
