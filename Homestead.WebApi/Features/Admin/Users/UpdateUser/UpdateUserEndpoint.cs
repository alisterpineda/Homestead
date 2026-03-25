using MediatR;

namespace Homestead.WebApi.Features.Admin.Users.UpdateUser;

public static class UpdateUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateUserCommand
            {
                Id = id,
                Email = request.Email,
                DisplayName = request.DisplayName,
                Role = request.Role,
                IsActive = request.IsActive
            };

            var response = await sender.Send(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateUser")
        .Produces<UserResponse>();
    }
}
