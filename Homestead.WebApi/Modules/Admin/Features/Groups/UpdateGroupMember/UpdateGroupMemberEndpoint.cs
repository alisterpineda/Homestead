namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public static class UpdateGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            UpdateGroupMemberRequest request, UpdateGroupMemberHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, userId, request, ct)))
        .WithName("UpdateGroupMember")
        .Produces<GroupMemberResponse>();
    }
}
