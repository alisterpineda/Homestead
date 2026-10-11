using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.RestoreDocuments;

public static class RestoreDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-restore", async (RestoreDocumentsCommand request, RestoreDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkRestoreDocuments")
        .Produces<RestoreDocumentsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/restore", async (Guid id, RestoreDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new RestoreDocumentsCommand([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.RestoredCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RestoreDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
