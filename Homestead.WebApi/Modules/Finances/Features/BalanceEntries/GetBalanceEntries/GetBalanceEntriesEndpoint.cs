using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;

public static class GetBalanceEntriesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/balance-entries",
            async (Guid accountId, GetBalanceEntriesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(accountId, ct)))
        .WithName("GetBalanceEntries")
        .Produces<IReadOnlyList<BalanceEntryResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
