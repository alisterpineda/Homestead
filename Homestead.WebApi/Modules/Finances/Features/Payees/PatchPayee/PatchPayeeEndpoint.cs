using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;

public static class PatchPayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id, PatchPayeeCommand body,
            PatchPayeeHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(body with { Id = id }, ct));
        })
        .WithName("PatchPayee")
        .Produces<PayeeResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
