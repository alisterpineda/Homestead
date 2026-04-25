using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;

public static class GetTransactionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("",
            async ([AsParameters] GetTransactionsQuery query, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(query, ct);
            return Results.Ok(response);
        })
        .WithName("GetTransactions")
        .Produces<PaginatedResponse<TransactionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
