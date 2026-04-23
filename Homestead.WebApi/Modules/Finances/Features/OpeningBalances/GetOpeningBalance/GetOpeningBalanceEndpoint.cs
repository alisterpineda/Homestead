using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public static class GetOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/opening-balances/{commodityId:guid}",
            async (Guid accountId, Guid commodityId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetOpeningBalanceQuery
            {
                AccountId = accountId,
                CommodityId = commodityId,
            }, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetOpeningBalance")
        .Produces<OpeningBalanceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
