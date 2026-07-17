using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;

public record GetTransactionDocumentsQuery(Guid TransactionId)
    : IRequest<IReadOnlyList<TransactionDocumentLinkResponse>>;
