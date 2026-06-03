using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.PatchTrade;

public static class PatchTradeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchTradeCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchTrade")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
