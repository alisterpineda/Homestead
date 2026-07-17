using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;

public record AddTransactionDocumentLinkCommand : ICommand<TransactionDocumentLinkResponse>
{
    public required Guid TransactionId { get; init; }
    public required Guid DocumentId { get; init; }
}

/// <summary>Request body — the transaction id rides the route.</summary>
public record AddTransactionDocumentLinkRequest(Guid DocumentId);
