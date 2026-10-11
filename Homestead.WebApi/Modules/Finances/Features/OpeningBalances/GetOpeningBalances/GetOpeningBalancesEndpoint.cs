namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public static class GetOpeningBalancesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/opening-balances",
            async (Guid accountId, GetOpeningBalancesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(accountId, ct)))
        .WithName("GetOpeningBalances")
        .Produces<IReadOnlyList<OpeningBalanceResponse>>();
    }
}
