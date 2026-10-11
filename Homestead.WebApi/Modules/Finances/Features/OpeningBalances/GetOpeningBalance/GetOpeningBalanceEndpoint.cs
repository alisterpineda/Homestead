namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public static class GetOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/opening-balances/{commodityId:guid}",
            async (Guid accountId, Guid commodityId, GetOpeningBalanceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(accountId, commodityId, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetOpeningBalance")
        .Produces<OpeningBalanceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
