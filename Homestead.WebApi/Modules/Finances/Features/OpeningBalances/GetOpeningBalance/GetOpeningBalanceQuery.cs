using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public record GetOpeningBalanceQuery : IRequest<OpeningBalanceResponse?>
{
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
}
