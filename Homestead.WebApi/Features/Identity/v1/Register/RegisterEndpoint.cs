namespace Homestead.WebApi.Features.Identity.v1.Register;

public static class RegisterEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/register", async (RegisterRequest request, RegisterHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return response.Succeeded
                ? Results.Ok()
                : Results.ValidationProblem(response.Errors!);
        })
        .WithName("Register")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .AllowAnonymous();
    }
}
