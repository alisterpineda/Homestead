using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.GetWarranties;

public static class GetItemWarrantiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/warranties", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetItemWarrantiesQuery(id), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItemWarranties")
        .Produces<IReadOnlyList<ItemWarrantyResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
