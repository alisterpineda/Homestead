using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public static class GetTransactionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{transactionId:guid}",
            async (Guid transactionId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetTransactionQuery(transactionId), ct);
            return Results.Ok(response);
        })
        .WithName("GetTransaction")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
