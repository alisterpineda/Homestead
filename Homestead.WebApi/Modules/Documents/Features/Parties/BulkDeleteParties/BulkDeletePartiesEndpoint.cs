using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;

public static class BulkDeletePartiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeletePartiesCommand request, BulkDeletePartiesHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one party ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteParties")
        .Produces<BulkDeletePartiesResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
