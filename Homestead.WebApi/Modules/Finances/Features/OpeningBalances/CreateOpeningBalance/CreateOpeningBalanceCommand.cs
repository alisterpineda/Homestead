using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public record CreateOpeningBalanceCommand : ICommand<OpeningBalanceResponse>
{
    public Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
    public string? Memo { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ValuePerUnit { get; init; }
    public required DateOnly AsOfDate { get; init; }
}
