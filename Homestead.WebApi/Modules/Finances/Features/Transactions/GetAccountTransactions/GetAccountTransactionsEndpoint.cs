using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;

public static class GetAccountTransactionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/transactions",
            async (Guid accountId, [AsParameters] GetAccountTransactionsParameters parameters,
                ISender sender, CancellationToken ct) =>
        {
            var query = new GetAccountTransactionsQuery
            {
                AccountId = accountId,
                CommodityId = parameters.CommodityId,
                DateFrom = parameters.DateFrom,
                DateTo = parameters.DateTo,
                Page = parameters.Page,
                PageSize = parameters.PageSize,
            };
            var response = await sender.Send(query, ct);
            return Results.Ok(response);
        })
        .WithName("GetAccountTransactions")
        .Produces<PaginatedResponse<TransactionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
