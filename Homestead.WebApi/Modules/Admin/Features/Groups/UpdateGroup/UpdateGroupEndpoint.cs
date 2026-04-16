using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

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
        .Produces(StatusCodes.Status409Conflict);
    }
}
