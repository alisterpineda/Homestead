using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupply;

public static class GetSupplyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetSupplyQuery(id), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetSupply")
        .Produces<SupplyDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
