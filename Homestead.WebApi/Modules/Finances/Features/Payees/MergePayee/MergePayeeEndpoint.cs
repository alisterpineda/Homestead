using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;

public static class MergePayeeEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/merge", async (Guid id,
            MergePayeeRequest body, MergePayeeHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("MergePayee")
        .Produces<PayeeResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
