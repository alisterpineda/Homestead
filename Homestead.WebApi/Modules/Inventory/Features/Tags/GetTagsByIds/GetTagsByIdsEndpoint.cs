using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public static class GetTagsByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetInventoryTagsByIdsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetInventoryTagsByIds")
        .Produces<IReadOnlyList<GetInventoryTagsByIdsResponse>>();
    }
}
