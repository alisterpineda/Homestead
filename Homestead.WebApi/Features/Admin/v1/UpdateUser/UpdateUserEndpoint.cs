using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.UpdateUser;

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

            try
            {
                var response = await sender.Send(command, ct);
                return response is not null ? Results.Ok(response) : Results.NotFound();
            }
            catch (IdentityValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors);
            }
        })
        .WithName("UpdateUser")
        .Produces<UserResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
