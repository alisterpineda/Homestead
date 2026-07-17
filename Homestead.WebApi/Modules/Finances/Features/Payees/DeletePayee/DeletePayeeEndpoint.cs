using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;

public static class DeletePayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var deleted = await sender.Send(new DeletePayeeCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeletePayee")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
