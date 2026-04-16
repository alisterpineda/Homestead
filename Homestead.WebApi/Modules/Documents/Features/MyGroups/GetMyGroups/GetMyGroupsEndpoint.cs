using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public static class GetMyGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetMyGroupsQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetMyGroups")
        .Produces<IReadOnlyList<GetMyGroupsResponse>>();
    }
}
