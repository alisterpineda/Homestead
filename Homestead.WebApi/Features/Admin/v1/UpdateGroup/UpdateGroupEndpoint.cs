namespace Homestead.WebApi.Features.Admin.v1.UpdateGroup;

public static class UpdateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest request,
            UpdateGroupHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateGroupCommand { Id = id, Request = request };
            return Results.Ok(await handler.HandleAsync(command, ct));
        })
        .WithName("UpdateGroup")
        .Produces<GroupResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
