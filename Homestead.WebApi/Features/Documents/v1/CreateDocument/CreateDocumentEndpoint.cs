using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async ([FromForm] string? name, [FromForm] string? label, [FromForm] Guid? groupId, IFormFile file, CreateDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new CreateDocumentRequest { Name = name, Label = label, GroupId = groupId, File = file };
            return await handler.HandleAsync(request, ct);
        })
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
