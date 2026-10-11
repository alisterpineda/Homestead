using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;

public static class GetAccountTransactionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/transactions",
            async (Guid accountId, [AsParameters] GetAccountTransactionsParameters parameters,
                GetAccountTransactionsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(accountId, parameters, ct)))
        .WithName("GetAccountTransactions")
        .Produces<PaginatedResponse<TransactionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
