using Microsoft.AspNetCore.Mvc;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocumentRevision;

public static class CreateDocumentRevisionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, [FromForm] string? label, [FromForm] string? notes,
            IFormFile file, CreateDocumentRevisionHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentRevisionRequest
            {
                DocumentId = documentId,
                Label = label,
                Notes = notes,
                File = file
            };
            return await handler.HandleAsync(request, ct);
        })
        .WithName("CreateDocumentRevision")
        .Produces<CreateDocumentRevisionResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }
}
