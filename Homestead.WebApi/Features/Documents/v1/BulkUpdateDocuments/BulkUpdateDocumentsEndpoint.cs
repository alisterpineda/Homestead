using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkUpdateDocuments;

public static class BulkUpdateDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-update", async (BulkUpdateDocumentsCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            if (request.TagPaths is { Count: > 0 } && request.RemoveTagPaths is { Count: > 0 }
                && request.TagPaths.Intersect(request.RemoveTagPaths, StringComparer.OrdinalIgnoreCase).Any())
            {
                return ProblemDetailsFactory.BusinessRuleResult("Tags cannot be both added and removed in the same request.");
            }

            if (request.PartyIds is { Count: > 0 } && request.RemovePartyIds is { Count: > 0 }
                && request.PartyIds.Intersect(request.RemovePartyIds).Any())
            {
                return ProblemDetailsFactory.BusinessRuleResult("Parties cannot be both added and removed in the same request.");
            }

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkUpdateDocuments")
        .Produces<BulkUpdateDocumentsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
