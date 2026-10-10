namespace Homestead.WebApi.Modules.Admin.Features.Users.CreateUser;

public static class CreateUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateUserCommand request, CreateUserHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/admin/users/{response.Id}", response);
        })
        .WithName("CreateUser")
        .Produces<UserResponse>(StatusCodes.Status201Created);
    }
}
