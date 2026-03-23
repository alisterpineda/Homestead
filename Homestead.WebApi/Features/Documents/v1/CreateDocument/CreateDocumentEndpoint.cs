using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async ([FromForm] string? name, [FromForm] string? label, [FromForm] Guid? groupId, IFormFile file, ISender sender, CancellationToken ct) =>
        {
            var request = new CreateDocumentCommand { Name = name, Label = label, GroupId = groupId, File = file };
            var response = await sender.Send(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status202Accepted)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
