using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

namespace Homestead.WebApi.Features.Documents.v1.DeleteParty;

public static class DeletePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeletePartiesHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeletePartiesRequest([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteParty")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
