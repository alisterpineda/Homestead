using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntries;

public static class GetBalanceEntriesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/balance-entries",
            async (Guid accountId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetBalanceEntriesQuery(accountId), ct);
            return Results.Ok(response);
        })
        .WithName("GetBalanceEntries")
        .Produces<IReadOnlyList<BalanceEntryResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
