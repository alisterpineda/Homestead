using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocument;

public static class DeleteDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new BulkDeleteDocumentsCommand([id]);
            var response = await sender.Send(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
