using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public static class PatchBalanceEntryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{accountId:guid}/balance-entries/{transactionId:guid}",
            async (Guid accountId, Guid transactionId, PatchBalanceEntryCommand body,
                PatchBalanceEntryHandler handler, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId, TransactionId = transactionId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchBalanceEntry")
        .Produces<BalanceEntryResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
