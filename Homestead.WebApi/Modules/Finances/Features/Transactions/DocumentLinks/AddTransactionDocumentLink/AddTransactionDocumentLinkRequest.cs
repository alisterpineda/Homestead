namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;

/// <summary>Request body — the transaction id rides the route.</summary>
public record AddTransactionDocumentLinkRequest(Guid DocumentId);
