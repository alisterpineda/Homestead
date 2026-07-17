using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;

public static class RemoveTransactionDocumentLinkEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}/documents/{documentId:guid}", async (
            Guid transactionId, Guid documentId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveTransactionDocumentLinkCommand
            {
                TransactionId = transactionId,
                DocumentId = documentId,
            }, ct);
            return Results.NoContent();
        })
        .WithName("RemoveTransactionDocumentLink")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
