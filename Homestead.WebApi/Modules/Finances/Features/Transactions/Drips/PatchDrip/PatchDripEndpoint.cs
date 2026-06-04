using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.PatchDrip;

public static class PatchDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{transactionId:guid}",
            async (Guid transactionId, PatchDripCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { TransactionId = transactionId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("PatchDrip")
        .Produces<TransactionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
