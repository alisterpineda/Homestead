using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;

public static class BulkDeleteTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteInventoryTagsCommand request, BulkDeleteTagsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one tag ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteInventoryTags")
        .Produces<BulkDeleteInventoryTagsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
