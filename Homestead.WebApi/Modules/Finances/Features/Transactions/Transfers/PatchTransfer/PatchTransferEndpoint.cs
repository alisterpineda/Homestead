using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public static class PatchTransferEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchTransferCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchTransfer")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
