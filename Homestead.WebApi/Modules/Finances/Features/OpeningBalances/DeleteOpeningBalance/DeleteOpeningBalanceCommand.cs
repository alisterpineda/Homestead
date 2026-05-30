using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;

public record DeleteOpeningBalanceCommand : ICommand<bool>
{
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
}
