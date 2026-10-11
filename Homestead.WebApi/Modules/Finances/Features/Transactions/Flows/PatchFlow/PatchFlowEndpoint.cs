using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.PatchFlow;

public static class PatchFlowEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchFlowCommand body, PatchFlowHandler handler, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchFlow")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
