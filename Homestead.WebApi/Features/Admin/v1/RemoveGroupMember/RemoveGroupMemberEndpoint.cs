using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.RemoveGroupMember;

public static class RemoveGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId,
            ISender sender, CancellationToken ct) =>
        {
            var command = new RemoveGroupMemberCommand { GroupId = id, UserId = userId };
            await sender.Send(command, ct);
            return Results.NoContent();
        })
        .WithName("RemoveGroupMember")
        .Produces(StatusCodes.Status204NoContent);
    }
}
