using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;

public static class GetTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetInventoryTagsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetInventoryTags")
        .Produces<PaginatedResponse<GetInventoryTagsResponse>>();
    }
}
