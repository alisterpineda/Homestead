using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.ChangeUserPassword;

public static class ChangeUserPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/password", async (Guid id, ChangeUserPasswordRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new ChangeUserPasswordCommand
            {
                Id = id,
                Password = request.Password
            };

            try
            {
                var found = await sender.Send(command, ct);
                return found ? Results.NoContent() : Results.NotFound();
            }
            catch (IdentityValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors);
            }
        })
        .WithName("ChangeUserPassword")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();
    }
}
