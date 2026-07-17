using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;

public static class AddTransactionDocumentLinkEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{transactionId:guid}/documents", async (
            Guid transactionId, AddTransactionDocumentLinkRequest body,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new AddTransactionDocumentLinkCommand
            {
                TransactionId = transactionId,
                DocumentId = body.DocumentId,
            }, ct);
            return Results.Created(
                $"/api/v1/finances/transactions/{transactionId}/documents", response);
        })
        .WithName("AddTransactionDocumentLink")
        .Produces<TransactionDocumentLinkResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
