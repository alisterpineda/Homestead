using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.CreateDrip;

public static class CreateDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("",
            async (CreateDripCommand body, CreateDripHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(body, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{response.Id}",
                response);
        })
        .WithName("CreateDrip")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
