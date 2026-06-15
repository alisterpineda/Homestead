using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public static class GetConsumableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetConsumableQuery(id), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetConsumable")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
