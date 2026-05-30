using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;

public record PatchOpeningBalanceCommand : ICommand<OpeningBalanceResponse>
{
    public Guid AccountId { get; init; }
    public Guid CommodityId { get; init; }
    public string? Memo { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ValuePerUnit { get; init; }
    public required DateOnly AsOfDate { get; init; }
}
