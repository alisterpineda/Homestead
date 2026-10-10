namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public record GetOpeningBalanceQuery
{
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
}
