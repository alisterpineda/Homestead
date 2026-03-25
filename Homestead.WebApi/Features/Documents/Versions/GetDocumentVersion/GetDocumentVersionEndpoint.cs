using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.GetDocumentVersion;

public static class GetDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid documentId, Guid id,
            ISender sender, CancellationToken ct) =>
        {
            var request = new GetDocumentVersionQuery(documentId, id);
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocumentVersion")
        .Produces<GetDocumentVersionResponse>();
    }
}
