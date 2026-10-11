using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public static class CreateBalanceEntryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{accountId:guid}/balance-entries",
            async (Guid accountId, CreateBalanceEntryCommand body, CreateBalanceEntryHandler handler, CancellationToken ct) =>
        {
            var request = body with { AccountId = accountId };
            var response = await handler.HandleAsync(request, ct);
            return Results.Created(
                $"/api/v1/finances/accounts/{accountId}/balance-entries/{response.TransactionId}",
                response);
        })
        .WithName("CreateBalanceEntry")
        .Produces<BalanceEntryResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
