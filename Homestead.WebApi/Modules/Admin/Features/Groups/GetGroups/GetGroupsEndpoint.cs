using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroups;

public static class GetGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetGroupsQuery request,
            GetGroupsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetGroups")
        .Produces<PaginatedResponse<GroupResponse>>();
    }
}
