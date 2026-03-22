using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.DeleteGroup;

public static class DeleteGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteGroupRequest(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteGroup")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
