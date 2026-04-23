using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public record GetOpeningBalancesQuery : IRequest<IReadOnlyList<OpeningBalanceResponse>>
{
    public required Guid AccountId { get; init; }
}
