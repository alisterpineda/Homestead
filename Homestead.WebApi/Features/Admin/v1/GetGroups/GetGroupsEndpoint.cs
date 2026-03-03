using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.GetGroups;

public static class GetGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetGroupsRequest request,
            GetGroupsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetGroups")
        .Produces<PaginatedResponse<GroupResponse>>();
    }
}
