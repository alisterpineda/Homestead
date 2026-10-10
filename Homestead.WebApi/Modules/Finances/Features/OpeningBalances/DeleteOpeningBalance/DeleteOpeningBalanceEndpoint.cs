using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;

public static class DeleteOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{accountId:guid}/opening-balances/{commodityId:guid}",
            async (Guid accountId, Guid commodityId, DeleteOpeningBalanceHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.Handle(new DeleteOpeningBalanceCommand
            {
                AccountId = accountId,
                CommodityId = commodityId,
            }, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteOpeningBalance")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
