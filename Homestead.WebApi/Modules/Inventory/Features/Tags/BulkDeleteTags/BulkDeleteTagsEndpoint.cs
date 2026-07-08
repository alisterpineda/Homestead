using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;

public static class BulkDeleteTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteInventoryTagsCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one tag ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteInventoryTags")
        .Produces<BulkDeleteInventoryTagsResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
