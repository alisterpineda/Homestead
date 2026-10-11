using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTags;

public static class GetTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTagsQuery request,
            GetTagsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTags")
        .Produces<PaginatedResponse<GetTagsResponse>>();
    }
}
