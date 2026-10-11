using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;

public static class GetBalanceEntryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/balance-entries/{transactionId:guid}",
            async (Guid accountId, Guid transactionId, GetBalanceEntryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(accountId, transactionId, ct)))
        .WithName("GetBalanceEntry")
        .Produces<BalanceEntryResponse>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
