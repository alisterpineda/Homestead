using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public static class GetTransactionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{transactionId:guid}",
            async (Guid transactionId, GetTransactionHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetTransactionQuery(transactionId), ct);
            return Results.Ok(response);
        })
        .WithName("GetTransaction")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
