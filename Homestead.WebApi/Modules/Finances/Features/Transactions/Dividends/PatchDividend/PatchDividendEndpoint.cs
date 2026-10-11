using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public static class PatchDividendEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchDividendCommand body, PatchDividendHandler handler, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchDividend")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
