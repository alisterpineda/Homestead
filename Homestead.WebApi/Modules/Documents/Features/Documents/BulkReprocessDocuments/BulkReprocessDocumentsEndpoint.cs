using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

public static class BulkReprocessDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-reprocess", async (BulkReprocessDocumentsCommand request, BulkReprocessDocumentsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Accepted(value: response);
        })
        .WithName("BulkReprocessDocuments")
        .Produces<BulkReprocessDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
