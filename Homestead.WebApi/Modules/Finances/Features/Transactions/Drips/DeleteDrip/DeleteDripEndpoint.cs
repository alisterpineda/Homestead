using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

public static class DeleteDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}",
            async (Guid transactionId, DeleteDripHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(transactionId, ct);
            return Results.NoContent();
        })
        .WithName("DeleteDrip")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
