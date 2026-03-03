namespace Homestead.WebApi.Features.Admin.v1.AddGroupMember;

public static class AddGroupMemberEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/members", async (Guid id, AddGroupMemberRequest request,
            AddGroupMemberHandler handler, CancellationToken ct) =>
        {
            var command = new AddGroupMemberCommand { GroupId = id, Request = request };
            var response = await handler.HandleAsync(command, ct);
            return Results.Created($"/api/v1/admin/groups/{id}/members/{response.UserId}", response);
        })
        .WithName("AddGroupMember")
        .Produces<GroupMemberResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
