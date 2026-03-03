using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public static class BulkDeleteDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteDocumentsRequest request, BulkDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteDocuments")
        .Produces<BulkDeleteDocumentsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
