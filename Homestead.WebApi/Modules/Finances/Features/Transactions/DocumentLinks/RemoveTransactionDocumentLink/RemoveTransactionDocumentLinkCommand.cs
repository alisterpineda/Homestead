namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;

public record RemoveTransactionDocumentLinkCommand
{
    public required Guid TransactionId { get; init; }
    public required Guid DocumentId { get; init; }
}
