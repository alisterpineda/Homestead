using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetDocument;

public static class GetDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new GetDocumentQuery(id);
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocument")
        .Produces<GetDocumentResponse>();
    }
}
