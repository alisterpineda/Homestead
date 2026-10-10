namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public static class UpdateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest request,
            UpdateGroupHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateGroupCommand { Id = id, Request = request };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("UpdateGroup")
        .Produces<GroupResponse>()
        .Produces(StatusCodes.Status409Conflict);
    }
}
