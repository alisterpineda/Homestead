using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public static class CreateUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateUserCommand request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var response = await sender.Send(request, ct);
                return Results.Created($"/api/v1/admin/users/{response.Id}", response);
            }
            catch (IdentityValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors);
            }
        })
        .WithName("CreateUser")
        .Produces<UserResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();
    }
}
