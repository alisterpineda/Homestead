using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.PatchTrade;

public static class PatchTradeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchTradeCommand body, PatchTradeHandler handler, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchTrade")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
