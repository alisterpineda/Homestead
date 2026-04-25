using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;

public static class DeleteTransactionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}",
            async (Guid transactionId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteTransactionCommand(transactionId), ct);
            return Results.NoContent();
        })
        .WithName("DeleteTransaction")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
