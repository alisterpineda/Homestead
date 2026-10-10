using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;

public static class GetTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetInventoryTagsQuery request,
            GetTagsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetInventoryTags")
        .Produces<PaginatedResponse<GetInventoryTagsResponse>>();
    }
}
