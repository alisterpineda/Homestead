using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public static class GetTransactionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{transactionId:guid}",
            async (Guid transactionId, GetTransactionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(transactionId, ct)))
        .WithName("GetTransaction")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
