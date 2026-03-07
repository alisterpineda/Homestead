using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;

public static class PermanentlyDeleteDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-permanently-delete", async (PermanentlyDeleteDocumentsRequest request, PermanentlyDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkPermanentlyDeleteDocuments")
        .Produces<PermanentlyDeleteDocumentsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/permanently-delete", async (Guid id, PermanentlyDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new PermanentlyDeleteDocumentsRequest([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("PermanentlyDeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
