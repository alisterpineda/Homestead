using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.GetBalanceEntry;

public static class GetBalanceEntryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/balance-entries/{transactionId:guid}",
            async (Guid accountId, Guid transactionId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetBalanceEntryQuery(accountId, transactionId), ct);
            return Results.Ok(response);
        })
        .WithName("GetBalanceEntry")
        .Produces<BalanceEntryResponse>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
