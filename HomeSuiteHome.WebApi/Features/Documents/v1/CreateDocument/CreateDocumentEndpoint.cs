using Microsoft.AspNetCore.Mvc;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async ([FromForm] string? name, IFormFile file, CreateDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentRequest { Name = name, File = file };
            var response = await handler.HandleAsync(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }
}
