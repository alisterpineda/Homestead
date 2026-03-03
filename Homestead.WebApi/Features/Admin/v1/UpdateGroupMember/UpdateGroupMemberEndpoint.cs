namespace Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;

public static class UpdateGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            UpdateGroupMemberRequest request, UpdateGroupMemberHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateGroupMemberCommand
            {
                GroupId = id,
                UserId = userId,
                Request = request
            };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("UpdateGroupMember")
        .Produces<GroupMemberResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
