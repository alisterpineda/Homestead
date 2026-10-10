namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public record GetOpeningBalancesQuery
{
    public required Guid AccountId { get; init; }
}
