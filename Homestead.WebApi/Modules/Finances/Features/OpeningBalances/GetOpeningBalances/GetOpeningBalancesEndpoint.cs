namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public static class GetOpeningBalancesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/opening-balances",
            async (Guid accountId, GetOpeningBalancesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetOpeningBalancesQuery { AccountId = accountId }, ct);
            return Results.Ok(response);
        })
        .WithName("GetOpeningBalances")
        .Produces<IReadOnlyList<OpeningBalanceResponse>>();
    }
}
