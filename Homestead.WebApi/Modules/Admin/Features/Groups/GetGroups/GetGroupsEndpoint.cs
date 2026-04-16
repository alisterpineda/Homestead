using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroups;

public static class GetGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetGroupsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetGroups")
        .Produces<PaginatedResponse<GroupResponse>>();
    }
}
