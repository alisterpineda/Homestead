using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async ([FromForm] string? name, [FromForm] string? label, [FromForm] Guid? groupId, IFormFile file, CreateDocumentHandler handler, CancellationToken ct) =>
            Results.Accepted(null, await handler.HandleAsync(name, label, groupId, file, ct)))
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
