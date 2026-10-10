namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public static class GetMyGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (GetMyGroupsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetMyGroupsQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetMyGroups")
        .Produces<IReadOnlyList<GetMyGroupsResponse>>();
    }
}
