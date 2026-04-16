using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.DeleteParty;

public static class DeletePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new BulkDeletePartiesCommand([id]);
            var response = await sender.Send(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteParty")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
