using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.RestoreDocuments;

public static class RestoreDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-restore", async (RestoreDocumentsCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkRestoreDocuments")
        .Produces<RestoreDocumentsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new RestoreDocumentsCommand([id]);
            var response = await sender.Send(request, ct);
            return response.RestoredCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RestoreDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
