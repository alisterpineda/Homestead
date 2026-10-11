using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;

public static class GetTransactionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("",
            async ([AsParameters] GetTransactionsQuery query, GetTransactionsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(query, ct);
            return Results.Ok(response);
        })
        .WithName("GetTransactions")
        .Produces<PaginatedResponse<TransactionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
