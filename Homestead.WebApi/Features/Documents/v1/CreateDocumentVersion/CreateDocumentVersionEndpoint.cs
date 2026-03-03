using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocumentVersion;

public static class CreateDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, [FromForm] string? label,
            IFormFile file, CreateDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentVersionRequest
            {
                DocumentId = documentId,
                Label = label,
                File = file
            };
            var response = await handler.HandleAsync(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("CreateDocumentVersion")
        .Produces<CreateDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
