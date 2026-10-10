using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PermanentlyDeleteDocuments;

public static class PermanentlyDeleteDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-permanently-delete", async (PermanentlyDeleteDocumentsCommand request, PermanentlyDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkPermanentlyDeleteDocuments")
        .Produces<PermanentlyDeleteDocumentsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/permanently-delete", async (Guid id, PermanentlyDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new PermanentlyDeleteDocumentsCommand([id]);
            var response = await handler.Handle(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("PermanentlyDeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
