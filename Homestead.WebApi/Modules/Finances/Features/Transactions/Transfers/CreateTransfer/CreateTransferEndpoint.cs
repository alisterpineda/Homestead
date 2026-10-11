using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public static class CreateTransferEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("",
            async (CreateTransferCommand body, CreateTransferHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(body, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{response.Id}",
                response);
        })
        .WithName("CreateTransfer")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
