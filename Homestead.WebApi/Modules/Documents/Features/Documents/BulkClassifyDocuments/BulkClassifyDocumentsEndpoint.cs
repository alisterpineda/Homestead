using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;

public static class BulkClassifyDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-classify", async (BulkClassifyDocumentsCommand request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Accepted(value: response);
        })
        .WithName("BulkClassifyDocuments")
        .Produces<BulkClassifyDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
