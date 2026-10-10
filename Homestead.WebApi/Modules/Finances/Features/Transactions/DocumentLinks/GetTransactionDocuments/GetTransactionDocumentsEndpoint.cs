using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;

public static class GetTransactionDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{transactionId:guid}/documents",
            async (Guid transactionId, GetTransactionDocumentsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetTransactionDocumentsQuery(transactionId), ct);
            return Results.Ok(response);
        })
        .WithName("GetTransactionDocuments")
        .Produces<IReadOnlyList<TransactionDocumentLinkResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
