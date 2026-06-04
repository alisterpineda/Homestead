using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

public static class DeleteDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}",
            async (Guid transactionId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteDripCommand(transactionId), ct);
            return Results.NoContent();
        })
        .WithName("DeleteDrip")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
