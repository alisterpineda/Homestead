using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;

public static class CreateCommentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, CreateCommentCommand request,
            ISender sender, CancellationToken ct) =>
        {
            var command = request with { DocumentId = documentId };
            var response = await sender.Send(command, ct);
            return Results.Created($"/api/v1/documents/{documentId}/comments/{response.Id}", response);
        })
        .WithName("CreateComment")
        .Produces<CreateCommentResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
