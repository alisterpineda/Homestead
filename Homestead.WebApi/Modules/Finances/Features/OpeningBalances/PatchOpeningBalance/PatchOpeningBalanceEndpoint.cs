using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;

public static class PatchOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{accountId:guid}/opening-balances/{commodityId:guid}",
            async (Guid accountId, Guid commodityId, PatchOpeningBalanceCommand body,
                PatchOpeningBalanceHandler handler, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId, CommodityId = commodityId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchOpeningBalance")
        .Produces<OpeningBalanceResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
