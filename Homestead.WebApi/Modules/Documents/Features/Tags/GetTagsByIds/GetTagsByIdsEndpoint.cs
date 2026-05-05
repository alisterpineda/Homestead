using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public static class GetTagsByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetTagsByIdsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTagsByIds")
        .Produces<IReadOnlyList<GetTagsByIdsResponse>>();
    }
}
