using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public static class DeleteDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid documentId, Guid id,
            ISender sender, CancellationToken ct) =>
        {
            var request = new DeleteDocumentVersionCommand(documentId, id);
            await sender.Send(request, ct);
            return Results.NoContent();
        })
        .WithName("DeleteDocumentVersion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
