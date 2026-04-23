using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;

public record DeleteOpeningBalanceCommand : IRequest<bool>
{
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
}
