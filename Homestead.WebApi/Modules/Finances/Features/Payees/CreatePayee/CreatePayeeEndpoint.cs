using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;

public static class CreatePayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreatePayeeCommand request, CreatePayeeHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/finances/payees/{response.Id}", response);
        })
        .WithName("CreatePayee")
        .Produces<PayeeResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
