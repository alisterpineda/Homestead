using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkSummarizeDocuments;

public static class BulkSummarizeDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-summarize", async (BulkSummarizeDocumentsCommand request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one document ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Accepted(value: response);
        })
        .WithName("BulkSummarizeDocuments")
        .Produces<BulkClassifyDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
