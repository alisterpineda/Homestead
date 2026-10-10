using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;

public static class CreateCommentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, CreateCommentCommand request,
            CreateCommentHandler handler, CancellationToken ct) =>
        {
            var command = request with { DocumentId = documentId };
            var response = await handler.Handle(command, ct);
            return Results.Created($"/api/v1/documents/{documentId}/comments/{response.Id}", response);
        })
        .WithName("CreateComment")
        .Produces<CreateCommentResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
