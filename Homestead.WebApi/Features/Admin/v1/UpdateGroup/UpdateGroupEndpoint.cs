using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroup;

public static class UpdateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateGroupCommand { Id = id, Request = request };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("UpdateGroup")
        .Produces<GroupResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
