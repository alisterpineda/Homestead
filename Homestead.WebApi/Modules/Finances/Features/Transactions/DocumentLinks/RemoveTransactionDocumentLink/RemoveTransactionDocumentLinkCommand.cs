using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;

public record RemoveTransactionDocumentLinkCommand : ICommand<Unit>
{
    public required Guid TransactionId { get; init; }
    public required Guid DocumentId { get; init; }
}
