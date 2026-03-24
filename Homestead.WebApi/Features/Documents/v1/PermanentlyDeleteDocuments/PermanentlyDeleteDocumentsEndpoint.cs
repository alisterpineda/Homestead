using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.PermanentlyDeleteDocuments;

public static class PermanentlyDeleteDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-permanently-delete", async (PermanentlyDeleteDocumentsCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkPermanentlyDeleteDocuments")
        .Produces<PermanentlyDeleteDocumentsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/permanently-delete", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new PermanentlyDeleteDocumentsCommand([id]);
            var response = await sender.Send(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("PermanentlyDeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
