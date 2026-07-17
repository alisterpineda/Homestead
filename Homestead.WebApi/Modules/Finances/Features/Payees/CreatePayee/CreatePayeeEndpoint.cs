using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;

public static class CreatePayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreatePayeeCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/finances/payees/{response.Id}", response);
        })
        .WithName("CreatePayee")
        .Produces<PayeeResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
