using MediatR;

namespace Homestead.WebApi.Features.Admin.Users.CreateUser;

public static class CreateUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateUserCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/admin/users/{response.Id}", response);
        })
        .WithName("CreateUser")
        .Produces<UserResponse>(StatusCodes.Status201Created);
    }
}
