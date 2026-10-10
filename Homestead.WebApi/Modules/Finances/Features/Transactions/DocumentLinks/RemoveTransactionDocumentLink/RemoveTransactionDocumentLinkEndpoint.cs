using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;

public static class RemoveTransactionDocumentLinkEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{transactionId:guid}/documents/{documentId:guid}", async (
            Guid transactionId, Guid documentId, RemoveTransactionDocumentLinkHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new RemoveTransactionDocumentLinkCommand
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
