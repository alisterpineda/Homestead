using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public static class CreateOpeningBalanceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{accountId:guid}/opening-balances",
            async (Guid accountId, CreateOpeningBalanceCommand body, CreateOpeningBalanceHandler handler, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Created(
                $"/api/v1/finances/accounts/{accountId}/opening-balances/{response.CommodityId}",
                response);
        })
        .WithName("CreateOpeningBalance")
        .Produces<OpeningBalanceResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
