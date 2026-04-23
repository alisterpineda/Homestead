using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public static class GetOpeningBalancesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/opening-balances",
            async (Guid accountId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetOpeningBalancesQuery { AccountId = accountId }, ct);
            return Results.Ok(response);
        })
        .WithName("GetOpeningBalances")
        .Produces<IReadOnlyList<OpeningBalanceResponse>>();
    }
}
