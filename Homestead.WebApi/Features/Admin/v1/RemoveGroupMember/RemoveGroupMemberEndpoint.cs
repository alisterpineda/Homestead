namespace Homestead.WebApi.Features.Admin.v1.RemoveGroupMember;

public static class RemoveGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            RemoveGroupMemberHandler handler, CancellationToken ct) =>
        {
            var command = new RemoveGroupMemberCommand { GroupId = id, UserId = userId };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("RemoveGroupMember")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
