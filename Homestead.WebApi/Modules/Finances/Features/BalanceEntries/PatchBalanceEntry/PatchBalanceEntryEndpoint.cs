using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public static class PatchBalanceEntryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{accountId:guid}/balance-entries/{transactionId:guid}",
            async (Guid accountId, Guid transactionId, PatchBalanceEntryCommand body,
                ISender sender, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId, TransactionId = transactionId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchBalanceEntry")
        .Produces<BalanceEntryResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
