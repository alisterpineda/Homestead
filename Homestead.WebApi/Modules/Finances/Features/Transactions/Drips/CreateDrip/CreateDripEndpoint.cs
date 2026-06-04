using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.CreateDrip;

public static class CreateDripEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("",
            async (CreateDripCommand body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(body, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{response.Id}",
                response);
        })
        .WithName("CreateDrip")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
