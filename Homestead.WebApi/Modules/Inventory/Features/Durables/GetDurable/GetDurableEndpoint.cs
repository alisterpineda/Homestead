using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurable;

public static class GetDurableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            [AsParameters] GetDurableQuery request,
            ISender sender,
            CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDurable")
        .Produces<DurableResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
