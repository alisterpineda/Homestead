namespace Homestead.WebApi.Modules.Search.Features.UniversalSearch;

public static class UniversalSearchEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] UniversalSearchQuery query,
            UniversalSearchHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(query, ct));
        })
        .WithName("UniversalSearch")
        .Produces<SearchResponse>();
    }
}
