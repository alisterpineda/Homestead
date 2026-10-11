namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public static class GetMyGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (GetMyGroupsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetMyGroups")
        .Produces<IReadOnlyList<GetMyGroupsResponse>>();
    }
}
