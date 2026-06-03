using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public static class PatchDividendEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchDividendCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchDividend")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
