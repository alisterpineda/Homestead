namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public static class GetTagsByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetTagsByIdsQuery request,
            GetTagsByIdsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTagsByIds")
        .Produces<IReadOnlyList<GetTagsByIdsResponse>>();
    }
}
