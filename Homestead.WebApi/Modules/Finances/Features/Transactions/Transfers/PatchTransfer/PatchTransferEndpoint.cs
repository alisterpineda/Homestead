using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public static class PatchTransferEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchTransferCommand body, PatchTransferHandler handler, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchTransfer")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
