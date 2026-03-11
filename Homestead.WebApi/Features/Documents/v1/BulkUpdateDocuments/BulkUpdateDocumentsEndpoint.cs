using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.BulkUpdateDocuments;

public static class BulkUpdateDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-update", async (BulkUpdateDocumentsRequest request, BulkUpdateDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkUpdateDocuments")
        .Produces<BulkUpdateDocumentsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
