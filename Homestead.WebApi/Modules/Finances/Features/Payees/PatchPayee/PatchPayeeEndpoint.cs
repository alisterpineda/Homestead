using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;

public static class PatchPayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id, PatchPayeeCommand body,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(body with { Id = id }, ct));
        })
        .WithName("PatchPayee")
        .Produces<PayeeResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
