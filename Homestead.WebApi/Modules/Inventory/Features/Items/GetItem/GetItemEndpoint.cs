using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public static class GetItemEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetItemQuery(id), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItem")
        .Produces<ItemDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
