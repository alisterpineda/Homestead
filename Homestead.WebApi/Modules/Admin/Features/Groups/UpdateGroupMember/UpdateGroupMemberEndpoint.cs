using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public static class UpdateGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            UpdateGroupMemberRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateGroupMemberCommand
            {
                GroupId = id,
                UserId = userId,
                Request = request
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("UpdateGroupMember")
        .Produces<GroupMemberResponse>();
    }
}
