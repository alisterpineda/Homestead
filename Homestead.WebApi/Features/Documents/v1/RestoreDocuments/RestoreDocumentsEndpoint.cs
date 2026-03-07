using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public static class RestoreDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-restore", async (RestoreDocumentsRequest request, RestoreDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkRestoreDocuments")
        .Produces<RestoreDocumentsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/restore", async (Guid id, RestoreDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new RestoreDocumentsRequest([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.RestoredCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RestoreDocument")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
