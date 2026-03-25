using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.Documents.CreateDocumentVersion;

public static class CreateDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, [FromForm] string? label,
            IFormFile file, ISender sender, CancellationToken ct) =>
        {
            var request = new CreateDocumentVersionCommand
            {
                DocumentId = documentId,
                Label = label,
                File = file
            };
            var response = await sender.Send(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("CreateDocumentVersion")
        .Produces<CreateDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
