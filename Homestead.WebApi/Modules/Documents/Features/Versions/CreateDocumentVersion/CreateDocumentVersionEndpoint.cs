using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.CreateDocumentVersion;

public static class CreateDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, [FromForm] string? label,
            IFormFile file, CreateDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentVersionCommand
            {
                DocumentId = documentId,
                Label = label,
                File = file
            };
            var response = await handler.Handle(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("CreateDocumentVersion")
        .Produces<CreateDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
