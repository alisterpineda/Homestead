using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;

public static class DeletePayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeletePayeeHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.HandleAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeletePayee")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
