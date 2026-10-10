using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;

public static class DeleteTransactionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}",
            async (Guid transactionId, DeleteTransactionHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new DeleteTransactionCommand(transactionId), ct);
            return Results.NoContent();
        })
        .WithName("DeleteTransaction")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
