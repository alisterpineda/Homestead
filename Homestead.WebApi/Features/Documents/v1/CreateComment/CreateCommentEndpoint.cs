using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public static class CreateCommentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (Guid documentId, CreateCommentRequest request,
            CreateCommentHandler handler, CancellationToken ct) =>
        {
            var command = request with { DocumentId = documentId };
            var response = await handler.HandleAsync(command, ct);
            return Results.Created($"/api/v1/documents/{documentId}/comments/{response.Id}", response);
        })
        .WithName("CreateComment")
        .Produces<CreateCommentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
