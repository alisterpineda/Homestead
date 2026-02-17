using Microsoft.AspNetCore.Mvc;

namespace HomeSuiteHome.WebApi.Features.Documents.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async ([FromForm] string? name, IFormFile file, CreateDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentRequest { Name = name, File = file };
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/documents/{response.DocumentId}", response);
        })
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status201Created)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }
}
