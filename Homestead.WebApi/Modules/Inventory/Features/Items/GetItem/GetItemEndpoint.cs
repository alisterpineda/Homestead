using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public static class GetItemEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            [AsParameters] GetItemQuery request,
            ISender sender,
            CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetItem")
        .Produces<ItemResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
