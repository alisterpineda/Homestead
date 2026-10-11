namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public static class UpdateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest request,
            UpdateGroupHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct)))
        .WithName("UpdateGroup")
        .Produces<GroupResponse>()
        .Produces(StatusCodes.Status409Conflict);
    }
}
