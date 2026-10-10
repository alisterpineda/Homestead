namespace Homestead.WebApi.Modules.Admin.Features.Groups.RemoveGroupMember;

public static class RemoveGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            RemoveGroupMemberHandler handler, CancellationToken ct) =>
        {
            var command = new RemoveGroupMemberCommand { GroupId = id, UserId = userId };
            await handler.Handle(command, ct);
            return Results.NoContent();
        })
        .WithName("RemoveGroupMember")
        .Produces(StatusCodes.Status204NoContent);
    }
}
