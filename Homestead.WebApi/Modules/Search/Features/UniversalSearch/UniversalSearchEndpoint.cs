using MediatR;

namespace Homestead.WebApi.Modules.Search.Features.UniversalSearch;

public static class UniversalSearchEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] UniversalSearchQuery query,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(query, ct));
        })
        .WithName("UniversalSearch")
        .Produces<SearchResponse>();
    }
}
