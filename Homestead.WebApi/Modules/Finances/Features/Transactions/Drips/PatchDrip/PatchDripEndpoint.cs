using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.PatchDrip;

public static class PatchDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchDripCommand body, PatchDripHandler handler, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchDrip")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
