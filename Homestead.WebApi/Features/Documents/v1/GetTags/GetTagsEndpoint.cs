using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

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
        .Produces<PaginatedResponse<GetTagsResponse>>();
    }
}
