using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public static class GetTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTagsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTags")
        .Produces<PaginatedResponse<GetTagsResponse>>();
    }
}
