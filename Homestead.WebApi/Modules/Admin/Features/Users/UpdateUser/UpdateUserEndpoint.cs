namespace Homestead.WebApi.Modules.Admin.Features.Users.UpdateUser;

public static class UpdateUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request,
            UpdateUserHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateUser")
        .Produces<UserResponse>();
    }
}
