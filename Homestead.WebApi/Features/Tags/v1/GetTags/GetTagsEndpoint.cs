namespace Homestead.WebApi.Features.Tags.v1.GetTags;

public static class GetTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTagsRequest request,
            GetTagsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTags")
        .Produces<IReadOnlyList<GetTagsResponse>>();
    }
}
