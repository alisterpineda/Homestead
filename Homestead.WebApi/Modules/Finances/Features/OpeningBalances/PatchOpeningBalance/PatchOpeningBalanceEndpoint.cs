using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;

public static class PatchOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{accountId:guid}/opening-balances/{commodityId:guid}",
            async (Guid accountId, Guid commodityId, PatchOpeningBalanceCommand body,
                ISender sender, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId, CommodityId = commodityId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchOpeningBalance")
        .Produces<OpeningBalanceResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
