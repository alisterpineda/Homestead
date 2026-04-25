using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public static class CreateTransferEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("",
            async (CreateTransferCommand body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(body, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{response.Id}",
                response);
        })
        .WithName("CreateTransfer")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
