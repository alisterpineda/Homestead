namespace Homestead.WebApi.Features.Admin.v1.CreateGroup;

public static class CreateGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateGroupRequest request,
            CreateGroupHandler handler, CancellationToken ct) =>
        {
            return await handler.HandleAsync(request, ct);
        })
        .WithName("CreateGroup")
        .Produces<GroupResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
    }
}
