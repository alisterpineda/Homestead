using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Register;

public static class RegisterEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/register", async (RegisterCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            if (response.SetupAlreadyComplete)
                return Results.Forbid();
            return response.Succeeded
                ? Results.Ok()
                : Results.ValidationProblem(response.Errors!);
        })
        .WithName("Register")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .AllowAnonymous();
    }
}
