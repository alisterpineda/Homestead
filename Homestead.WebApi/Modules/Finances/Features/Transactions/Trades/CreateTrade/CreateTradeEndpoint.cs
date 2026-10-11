using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.CreateTrade;

public static class CreateTradeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("",
            async (CreateTradeCommand body, CreateTradeHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(body, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{response.Id}",
                response);
        })
        .WithName("CreateTrade")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
