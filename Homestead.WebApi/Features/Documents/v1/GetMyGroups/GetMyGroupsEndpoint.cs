namespace Homestead.WebApi.Features.Documents.v1.GetMyGroups;

public static class GetMyGroupsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (GetMyGroupsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(new GetMyGroupsRequest(), ct);
            return Results.Ok(response);
        })
        .WithName("GetMyGroups")
        .Produces<IReadOnlyList<GetMyGroupsResponse>>();
    }
}
