using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

public static class GetPlaceContentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/contents", async ([AsParameters] GetPlaceContentsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetPlaceContents")
        .Produces<PlaceContentsResponse>();
    }
}
