using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;

public static class GetTransactionDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{transactionId:guid}/documents",
            async (Guid transactionId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetTransactionDocumentsQuery(transactionId), ct);
            return Results.Ok(response);
        })
        .WithName("GetTransactionDocuments")
        .Produces<IReadOnlyList<TransactionDocumentLinkResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
